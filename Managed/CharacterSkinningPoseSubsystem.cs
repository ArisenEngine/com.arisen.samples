using System;
using System.Numerics;
using ArisenEngine.Core.ECS;
using ArisenEngine.ECS.Lifecycle;
using ArisenKernel.Diagnostics;
using ArisenKernel.Lifecycle;

namespace ArisenEngine.Samples;

/// <summary>
/// Drives the skinning sample's character. The catalogue entry's scene renders the package's
/// imported Knight, whose cooked skeleton is 41 joints (the rig ships IK and control bones beside
/// its deform bones), and this driver is what makes the sample a skinning sample: it attaches the
/// entity's <see cref="SkeletonPoseComponent"/> and writes the head's and the shoulders' local
/// rotations from elapsed time, so the arms hang at the character's sides and sway instead of
/// staying in the imported T-pose, and the frame's skin palette follows them - which also carries
/// the lower arms, the hands, and everything they hold.
///
/// The driver is content-gated: it only binds the figure that renders the package's rigged mesh, so
/// profiles that boot other content pay for one pool scan until that figure appears, and a scene that
/// never contains it is never posed. The joint count is the driver's contract with the cooked
/// skeleton: a mismatch is a broken sample and fails loudly in the render pipeline instead of
/// silently drawing the wrong palette.
/// </summary>
public sealed class CharacterSkinningPoseSubsystem : ITickableSubsystem
{
    // Prepared by Arisen/Scripts/Windows/prepare_kaykit_character.py, which prints the same table:
    // the mesh cook orders joints parents-first by bucketing each joint's ancestor depth and keeping
    // the source order inside a depth, so these indices are the source order of the character's rig.
    private const int JointCount = 41;
    private const int LeftUpperArmJoint = 22;   // upperarm.l, +X in the imported T-pose
    private const int RightUpperArmJoint = 23;  // upperarm.r, -X
    private const int HeadJoint = 24;

    // A pose replaces a joint's local rotation, and this rig does not rest at identity, so the swing
    // composes with the imported rest rotations above them (values read from Assets/Meshes/Knight.glb;
    // the prep script prints the same table). The rig mirrors the arms around the character's up axis,
    // so one parent-space delta about Z lowers both: the left arm through -Z, the right through +Z.
    private static readonly Quaternion LeftUpperArmRest =
        Quaternion.Normalize(new Quaternion(-0.514121f, -0.485468f, -0.485468f, 0.514122f));
    private static readonly Quaternion RightUpperArmRest =
        Quaternion.Normalize(new Quaternion(-0.514121f, 0.485468f, 0.485468f, 0.514122f));

    private const float TwoPi = 2.0f * MathF.PI;

    // The imported arms are horizontal, which is a bind pose, not a stance: the rest angle lowers
    // them towards the character's sides and the swing keeps them moving around it.
    private const float ArmRestAngle = 1.12f;
    private const float ArmSwingAmplitude = 0.26f;
    private const float ArmSwingFrequency = 0.35f;
    private const float HeadYawAmplitude = 0.38f;
    private const float HeadYawFrequency = 0.27f;
    private const float HeadPitchOffset = 0.06f;
    private const float HeadPitchAmplitude = 0.12f;
    private const float HeadPitchFrequency = 0.47f;

    private const int PoseLogIntervalFrames = 60;

    private SceneSubsystem? m_Scenes;
    private Entity m_Target = Entity.Null;
    private bool m_HasTarget;
    private float m_Elapsed;
    private int m_FramesSincePoseLog;

    public int Priority => 40;

    public EnginePhase InitPhase => EnginePhase.Running;

    public void Initialize()
    {
#if ARISEN_ENGINE_EDITOR
        // The editor's scene documents own authoring previews, so a runtime driver that poses a
        // scene's content stays out of the way: no scene subsystem is resolved and Tick never runs.
        KernelLog.Info(
            "[CharacterSkinning] Editor build detected; the editor's scene documents own authoring " +
            "previews, so the sample driver stays idle.");
#else
        EngineKernel kernel = EngineKernel.Instance;
        m_Scenes = kernel.GetSubsystem<SceneSubsystem>()
            ?? throw new InvalidOperationException(
                "The skinning sample driver requires SceneSubsystem.");
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
            manager.AddComponent(m_Target, SkeletonPoseComponent.Create(JointCount));
        }

        ref SkeletonPoseComponent pose = ref poses.GetRef(m_Target);
        ApplyPose(ref pose, m_Elapsed);
        LogPose();
    }

    public void Shutdown()
    {
        ClearTarget();
        m_Elapsed = 0.0f;
        m_FramesSincePoseLog = 0;
    }

    public void Dispose() => Shutdown();

    /// <summary>
    /// Finds the entity that renders the sample's rigged mesh and gives it the pose the render
    /// pipeline reads. Returns false while no such entity exists - the sample's scene is simply not
    /// the loaded content - and every later frame retries, so a scene switch binds the figure it
    /// brings.
    /// </summary>
    private bool TryBindTarget(EntityManager manager)
    {
        ComponentPool<MeshRendererComponent> meshes = manager.GetPool<MeshRendererComponent>();
        ReadOnlySpan<Entity> entities = meshes.GetRawEntityArray();
        ReadOnlySpan<MeshRendererComponent> renderers = meshes.GetRawComponentArray();
        for (int index = 0; index < meshes.Count; index++)
        {
            if (renderers[index].MeshGuid != SamplesAssetRefs.KnightMeshGuid)
            {
                continue;
            }

            m_Target = entities[index];
            m_HasTarget = true;
            if (!manager.HasComponent<SkeletonPoseComponent>(m_Target))
            {
                manager.AddComponent(m_Target, SkeletonPoseComponent.Create(JointCount));
            }

            KernelLog.InfoFormat(
                "[CharacterSkinning] Bound the sample figure {0} ({1} joints); posing its head and " +
                "shoulders from elapsed time.",
                m_Target,
                JointCount);
            return true;
        }

        return false;
    }

    private void ApplyPose(ref SkeletonPoseComponent pose, float elapsed)
    {
        float armAngle = ArmRestAngle + ArmSwingAmplitude * MathF.Sin(TwoPi * ArmSwingFrequency * elapsed);
        // The imported character faces +Z, so a rotation about Z swings an arm in its frontal plane;
        // the sides mirror the sign so both arms lower instead of one crossing the body.
        pose.SetLocalRotation(
            LeftUpperArmJoint,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -armAngle) * LeftUpperArmRest);
        pose.SetLocalRotation(
            RightUpperArmJoint,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, armAngle) * RightUpperArmRest);

        float headYaw = HeadYawAmplitude * MathF.Sin(TwoPi * HeadYawFrequency * elapsed);
        float headPitch = HeadPitchOffset + HeadPitchAmplitude * MathF.Sin(TwoPi * HeadPitchFrequency * elapsed);
        pose.SetLocalRotation(HeadJoint, Quaternion.CreateFromYawPitchRoll(headYaw, headPitch, 0.0f));
    }

    /// <summary>
    /// A bounded per-second line is what makes a run's pose observable: the joints themselves are
    /// consumed by the palette, and the summary artifact only records the frame it captures.
    /// </summary>
    private void LogPose()
    {
        if (++m_FramesSincePoseLog < PoseLogIntervalFrames)
        {
            return;
        }

        m_FramesSincePoseLog = 0;
        float armAngle = ArmRestAngle + ArmSwingAmplitude * MathF.Sin(TwoPi * ArmSwingFrequency * m_Elapsed);
        KernelLog.InfoFormat(
            "[CharacterSkinning] Pose | Elapsed: {0:F2}s | Arm: {1:F3} rad | Head yaw: {2:F3} rad | Head pitch: {3:F3} rad",
            m_Elapsed,
            armAngle,
            HeadYawAmplitude * MathF.Sin(TwoPi * HeadYawFrequency * m_Elapsed),
            HeadPitchOffset + HeadPitchAmplitude * MathF.Sin(TwoPi * HeadPitchFrequency * m_Elapsed));
    }

    private void ClearTarget()
    {
        m_Target = Entity.Null;
        m_HasTarget = false;
    }
}
