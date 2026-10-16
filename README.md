# com.arisen.samples

Capability gallery for Arisen Engine.

This package is the single place where an engine capability can be seen running. It owns the sample
catalogue, the hub entry scene, and the launch target each sample is reached through. It holds no
engine capability of its own: a sample composes packages that already exist and this package decides
which of them are selected and which scene and world they start in. A sample whose content has to be
driven brings the driver that drives it, so it ships beside the content it owns instead of in a shell
every product carries.

## Why it exists

A profile decides the package graph at boot, so capability has no catalogue today: a capability can
only be seen by knowing which workspace, profile, and solution to launch. The gallery gives every
capability one entry point, one expected visual, and one bounded gate.

## Rules

- **A sample is content first.** Adding one adds a catalogue entry and its content; a sample that has
  to move (or otherwise drive) its own content adds the driver that does it, and that driver only ever
  binds the content it owns, so the profiles that boot other content never see it.
- **Every sample boots directly through `--startup-sample <id>`.** The hub selects between samples;
  it is never the only way in, and no automated gate drives the hub UI.
- **A sample declares the packages it needs.** Its dependency closure must not contain a package it
  did not declare, so a sample cannot pass only because a neighbour happened to satisfy a
  dependency.
- **The hub is stateless.** No sample may depend on state another sample left behind.
- **The first deliverable of an engine track is its sample**, and that sample must be gated.

## Sample classes

A sample declares what it needs from the package graph, because the graph is mounted once at boot:

- **Content**: the same package set, a different scene or world. Selected inside a running process.
- **Capability**: a different package set or a different build switch. Reached through its own
  profile and a process restart.

## Sample drivers

A data-only sample adds nothing but its catalogue entry, its scene or world, and the assets it names.
A sample whose content needs behaviour is driven by a subsystem in this package:

- `CharacterSkinningPoseSubsystem` drives the `character-skinning` entry: it reads the character
  mesh's cooked clip table and samples the current clip every frame into the entity's
  `SkeletonPoseComponent` - rotations and translations, so the walk keeps its hip bob - which is
  the pose the frame's skin palette is built from. Clips are addressed by name: the sample plays
  the baked walk clip the imported figure ships with (`Clip_0`), and a sequence cross-fades
  through the engine's `MeshAnimationPlayer` - the driver only picks names and fade length. It
  binds only the entity that renders this
  package's character mesh, so every other sample - and every product - sees an inert driver, and
  the mesh renders its cooked rest pose if the driver never engages.
## Sample content

`character-skinning` is the first sample whose content is imported rather than authored here, and its
figure is Cesium Man from the Khronos glTF Sample Models (see `THIRD_PARTY.md`): one skin, one
skinned node, 19 joints, and one LINEAR walk clip - `Clip_0` - on the same skeleton. The cook bakes
the clip's rotation channels into the mesh's payload as per-joint tracks, folding any non-joint
wrappers between joints into the joint chain, so the sample plays a clip authored for a real rig
without a clip asset kind of its own. KayKit's CC0 *Character Pack: Adventurers* figure ships beside
it as the larger rig (41 joints, 76 clips) the sample was first built against, and stays in
`Assets/Meshes/Knight.glb` for that work.
## The catalogue

`Assets\Catalogue\ShowcaseSamples.arisensamples` is the single declaration of every sample. An entry
declares its identity, a one-line description, the scene and/or world it boots, the packages it
needs, the expected visual, and the gate scenario and artifact that prove it.

- The workspace manifest selects the catalogue by GUID (`SampleCatalogue`), so nothing depends on a
  filename.
- A launch resolves an entry with `--startup-sample <id>`; the shell activates the entry's scene or
  world exactly as it would a directly named target.
- The cook follows the selection: cooking a sample closes over the catalogue and every entry's
  content, and a profile that declares the gallery role cooks the whole catalogue, so a cooked-only
  profile can boot any declared sample without a source fallback.
- A world-backed entry can also be selected inside the running process: the sample-switch smoke
  drives every world-backed entry through the runtime world-load path and asserts each one becomes
  the active world, which is the capability the hub's browsing layer is built on.

## Status

The catalogue, the hub scene, the `Samples` profile, the `--startup-sample <id>` launch, and
in-process switching between content samples are in place, and `character-skinning` is the first
entry that carries a driver (the C1 skinning sample) and its own gate: `validate_runtime` launches
this entry directly three times, requires the driver's bound-and-sampled log lines with the SKINNED
pipeline variant, and requires the captured frames to differ so playback is proven moving - the
catalogue's `Gate`/`Artifact` fields name exactly that run. The hub's browsing layer is the next
item; the plan and the acceptance rules live in the engine repository at
`Arisen\Docs\Architecture\ArisenDevelopmentRoadmap.md`.
