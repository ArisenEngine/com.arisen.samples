using System;
using System.Collections.Generic;
using System.Numerics;
using ArisenEngine.Core.Assets;
using ArisenEngine.Core.ECS;
using ArisenEngine.ECS.Lifecycle;
using ArisenEngine.Rendering.Resources;
using ArisenKernel.Diagnostics;
using ArisenKernel.Lifecycle;

namespace ArisenEngine.Samples;

/// <summary>
/// Plays the skinning sample's character. The catalogue entry's scene renders the package's imported
/// figure, whose cooked mesh carries its own skeleton and clips; this driver reads that payload
/// once, then samples the current clip every frame and writes the resulting local pose (rotations
/// and translations - a walk's hip bob rides the root joint's translation track)
/// into the entity's <see cref="SkeletonPoseComponent"/>, which is the pose the frame's skin palette
/// is built from. A clip is addressed by its name, so the sample's sequence is content, not indices.
///
/// The driver is content-gated: it only binds the entity that renders the character mesh, so profiles
/// that boot other content pay for one pool scan until that mesh appears, and the character renders
/// its cooked rest pose if the driver never engages.
/// </summary>
public sealed class CharacterSkinningPoseSubsystem : ITickableSubsystem
{
    /// <summary>
    /// The clips the sample plays, in order. They ship in the same file the character's mesh cooks
    /// from, so the sample plays clips authored for a real rig rather than poses written here.
    
    /// </summary>
    private static readonly string[] ClipSequence =
    [
        // Cesium Man ships one walk cycle and no stance variants, so the sample loops the clip the
        // character came with.
        "Clip_0",
    ];

    private const float PoseLogIntervalSeconds = 1.0f;

    /// <summary>
    /// The fade every clip change runs through. Clips are authored independently - one ends in a
    /// crouch while the next starts upright - so a cut would snap the skeleton; the fade
    /// blends the pose the frame is leaving with the pose the incoming clip samples.
    /// </summary>
    private const float BlendSeconds = 0.15f;

    private IAssetDatabase? m_AssetDatabase;
    private SceneSubsystem? m_Scenes;
    private Entity m_Target = Entity.Null;
    private bool m_HasTarget;
    private float m_Elapsed;
    private float m_NextPoseLogTime;

    private CookedAssetHandle m_CookedHandle = CookedAssetHandle.Invalid;
    private MeshSkin? m_Skin;
    private MeshAnimationTable? m_Animations;
    private int[] m_ClipIndices = Array.Empty<int>();
    private int m_ClipCursor;
    private float m_ClipTime;
    private Quaternion[] m_PoseScratch = Array.Empty<Quaternion>();
    private Quaternion[] m_BlendFromPose = Array.Empty<Quaternion>();
    private Vector3[] m_TranslationScratch = Array.Empty<Vector3>();
    private Vector3[] m_BlendFromTranslations = Array.Empty<Vector3>();
    private float m_BlendRemaining;

    public int Priority => 40;

    public EnginePhase InitPhase => EnginePhase.Running;

    public void Initialize()
    {
#if ARISEN_ENGINE_EDITOR
        // The editor's scene documents own authoring previews, so a runtime driver that poses a
        // scene's content stays out of the way: no services are resolved and Tick never runs.
        KernelLog.Info(
            "[CharacterSkinning] Editor build detected; the editor's scene documents own authoring " +
            "previews, so the sample driver stays idle.");
#else
        EngineKernel kernel = EngineKernel.Instance;
        m_Scenes = kernel.GetSubsystem<SceneSubsystem>()
            ?? throw new InvalidOperationException(
                "The skinning sample driver requires SceneSubsystem.");
        m_AssetDatabase = kernel.Services.GetService<IAssetDatabase>()
            ?? throw new InvalidOperationException(
                "The skinning sample driver samples the character's cooked clips, so it requires the asset database.");
#endif
    }

    public void Tick(float deltaTime)
    {
        if (m_Scenes == null || deltaTime <= 0.0f)
        {
            return;
        }

        EntityManager? manager = m_Scenes.ActiveEntityManager;
        if (manager == null)
        {
            ClearTarget();
            return;
        }

        if (!m_HasTarget || !manager.IsAlive(m_Target))
        {
            ClearTarget();
            if (!TryBindTarget(manager))
            {
                return;
            }
        }

        m_Elapsed += deltaTime;
        ComponentPool<SkeletonPoseComponent> poses = manager.GetPool<SkeletonPoseComponent>();
        if (!poses.Has(m_Target))
        {
            manager.AddComponent(m_Target, SkeletonPoseComponent.Create(m_Skin!.Joints.Count));
        }

        AdvanceClip(deltaTime);
        MeshAnimationSampler.SamplePose(
            m_Skin!,
            m_Animations!,
            m_ClipIndices[m_ClipCursor],
            m_ClipTime,
            m_PoseScratch,
            m_TranslationScratch);
        ApplyBlend(deltaTime);

        ref SkeletonPoseComponent pose = ref poses.GetRef(m_Target);
        for (int jointIndex = 0; jointIndex < m_PoseScratch.Length; jointIndex++)
        {
            pose.SetLocalRotation(jointIndex, m_PoseScratch[jointIndex]);
            pose.SetLocalTranslation(jointIndex, m_TranslationScratch[jointIndex]);
        }

        LogPose();
    }

    public void Shutdown()
    {
        ClearTarget();
        if (m_CookedHandle.IsValid && m_AssetDatabase != null)
        {
            m_AssetDatabase.Release(m_CookedHandle);
        }

        m_CookedHandle = CookedAssetHandle.Invalid;
        m_Skin = null;
        m_Animations = null;
        m_ClipIndices = Array.Empty<int>();
        m_PoseScratch = Array.Empty<Quaternion>();
        m_BlendFromPose = Array.Empty<Quaternion>();
        m_TranslationScratch = Array.Empty<Vector3>();
        m_BlendFromTranslations = Array.Empty<Vector3>();
        m_ClipCursor = 0;
        m_ClipTime = 0.0f;
        m_Elapsed = 0.0f;
        m_NextPoseLogTime = 0.0f;
    }

    public void Dispose() => Shutdown();

    /// <summary>
    /// Finds the entity that renders the sample's character mesh and reads the cooked payload its
    /// clips come from. Returns false while no such entity exists - the sample's scene is simply not
    /// the loaded content - and every later frame retries, so a scene switch binds the character it
    /// brings.
    /// </summary>
    private bool TryBindTarget(EntityManager manager)
    {
        ComponentPool<MeshRendererComponent> meshes = manager.GetPool<MeshRendererComponent>();
        ReadOnlySpan<Entity> entities = meshes.GetRawEntityArray();
        ReadOnlySpan<MeshRendererComponent> renderers = meshes.GetRawComponentArray();
        for (int index = 0; index < meshes.Count; index++)
        {
            if (renderers[index].MeshGuid != SamplesAssetRefs.CesiumManMeshGuid)
            {
                continue;
            }

            m_Target = entities[index];
            m_HasTarget = true;
            LoadCharacterClips();
            KernelLog.InfoFormat(
                "[CharacterSkinning] Bound the sample character {0} ({1} joints) and its cooked clips: {2}.",
                m_Target,
                m_Skin!.Joints.Count,
                DescribeSequence());
            return true;
        }

        return false;
    }

    private void LoadCharacterClips()
    {
        CookedMesh cooked = MeshAssetCooker.LoadOrCook(m_AssetDatabase!, CreateMeshAsset());
        m_CookedHandle = cooked.Handle;
        ReadOnlyMemory<byte> payload = m_AssetDatabase!.GetCookedAssetBytes(m_CookedHandle);
        m_Skin = MeshSkinPalette.ReadCookedSkin(payload.Span, cooked);
        m_Animations = MeshAnimationTable.Read(payload.Span, cooked);
        if (m_Animations.ClipCount == 0)
        {
            throw new InvalidOperationException(
                "[CharacterSkinning] The character's cooked mesh carries no clips; this sample animates " +
                "clips and the cook bakes them beside the skeleton.");
        }

        var sequence = new List<int>(ClipSequence.Length);
        foreach (string clip in ClipSequence)
        {
            if (m_Animations.TryFindClip(clip, out int clipIndex))
            {
                sequence.Add(clipIndex);
            }
            else
            {
                KernelLog.Warning(
                    $"[CharacterSkinning] The character's payload has no clip named '{clip}'; the sample skips it.");
            }
        }

        if (sequence.Count == 0)
        {
            throw new InvalidOperationException(
                "[CharacterSkinning] None of the sample's clips are cooked into the character's payload.");
        }

        m_ClipIndices = sequence.ToArray();
        m_PoseScratch = new Quaternion[m_Skin.Joints.Count];
        m_BlendFromPose = new Quaternion[m_Skin.Joints.Count];
        m_TranslationScratch = new Vector3[m_Skin.Joints.Count];
        m_BlendFromTranslations = new Vector3[m_Skin.Joints.Count];
        m_ClipCursor = 0;
        m_ClipTime = 0.0f;
    }

    /// <summary>
    /// The cooked artifact is addressed by asset identity alone. The source format only matters when
    /// the payload has to be cooked from source, so a cooked-only run - where the source is not
    /// readable - reads the artifact without one.
    /// </summary>
    private MeshAsset CreateMeshAsset()
    {
        if (m_AssetDatabase!.CanReadSourceAssets &&
            m_AssetDatabase.TryGetAsset(SamplesAssetRefs.CesiumManMeshGuid, out AssetRecord? sourceAsset))
        {
            return new MeshAsset(
                SamplesAssetRefs.CesiumManMeshGuid,
                "PackageGame/CesiumMan",
                MeshVariantKey.Default,
                ResolveMeshSourceFormat(sourceAsset.SourcePath));
        }

        return new MeshAsset(
            SamplesAssetRefs.CesiumManMeshGuid,
            $"RuntimeMesh/{SamplesAssetRefs.CesiumManMeshGuid:N}",
            MeshVariantKey.Default);
    }

    private static MeshSourceFormat ResolveMeshSourceFormat(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".glb" => MeshSourceFormat.GltfBinary,
            ".gltf" => MeshSourceFormat.GltfJson,
            ".obj" => MeshSourceFormat.WavefrontObj,
            _ => MeshSourceFormat.ArisenTextMesh,
        };

    private void AdvanceClip(float deltaTime)
    {
        m_ClipTime += deltaTime;
        MeshAnimationClip clip = m_Animations!.GetClip(m_ClipIndices[m_ClipCursor]);
        if (m_ClipIndices.Length == 1 || clip.Duration <= 0.0f || m_ClipTime < clip.Duration)
        {
            return;
        }

        m_ClipTime -= clip.Duration;
        m_ClipCursor = (m_ClipCursor + 1) % m_ClipIndices.Length;
        // The fade starts from the pose this frame has already drawn, so the switch never snaps.
        Array.Copy(m_PoseScratch, m_BlendFromPose, m_PoseScratch.Length);
        Array.Copy(m_TranslationScratch, m_BlendFromTranslations, m_TranslationScratch.Length);
        m_BlendRemaining = BlendSeconds;
        MeshAnimationClip next = m_Animations.GetClip(m_ClipIndices[m_ClipCursor]);
        KernelLog.InfoFormat(
            "[CharacterSkinning] Clip '{0}' ({1:0.###}s, {2} track(s)).",
            next.Name,
            next.Duration,
            next.TrackCount);
    }

    private void ApplyBlend(float deltaTime)
    {
        if (m_BlendRemaining <= 0.0f)
        {
            return;
        }

        m_BlendRemaining = MathF.Max(0.0f, m_BlendRemaining - deltaTime);
        float blend = 1.0f - m_BlendRemaining / BlendSeconds;
        for (int jointIndex = 0; jointIndex < m_PoseScratch.Length; jointIndex++)
        {
            m_PoseScratch[jointIndex] = Quaternion.Slerp(
                m_BlendFromPose[jointIndex],
                m_PoseScratch[jointIndex],
                blend);
            m_TranslationScratch[jointIndex] = Vector3.Lerp(
                m_BlendFromTranslations[jointIndex],
                m_TranslationScratch[jointIndex],
                blend);
        }
    }

    /// <summary>
    /// A bounded per-second line is what makes a run's clip observable: the sampled pose itself is
    /// consumed by the palette, and the frame's artifact only records what it captured.
    /// </summary>
    private void LogPose()
    {
        if (m_Elapsed < m_NextPoseLogTime)
        {
            return;
        }

        m_NextPoseLogTime = m_Elapsed + PoseLogIntervalSeconds;
        MeshAnimationClip clip = m_Animations!.GetClip(m_ClipIndices[m_ClipCursor]);
        KernelLog.InfoFormat(
            "[CharacterSkinning] Clip '{0}' | Elapsed: {1:F2}s | Clip time: {2:F2}s / {3:F2}s",
            clip.Name,
            m_Elapsed,
            m_ClipTime,
            clip.Duration);
    }

    private string DescribeSequence()
    {
        var names = new string[m_ClipIndices.Length];
        for (int index = 0; index < m_ClipIndices.Length; index++)
        {
            names[index] = m_Animations!.GetClip(m_ClipIndices[index]).Name;
        }

        return string.Join(", ", names);
    }

    private void ClearTarget()
    {
        m_Target = Entity.Null;
        m_HasTarget = false;
    }
}
