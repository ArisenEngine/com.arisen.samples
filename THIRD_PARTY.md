# Third-party content

## KayKit - Character Pack: Adventurers

- Author: Kay Lousberg (<https://www.kaylousberg.com>, <https://kaylousberg.itch.io/kaykit-adventurers>)
- License: Creative Commons Zero (CC0 1.0, <https://creativecommons.org/publicdomain/zero/1.0/>) -
  free for personal, educational, and commercial use, no attribution required. Credited here because
  the pack ships the showcase's character.

`Assets/Meshes/Knight.glb` and `Assets/Textures/Knight_BaseColor.png` are derived from the pack's
`Characters/gltf/Knight.glb` by `Arisen/Scripts/Windows/prepare_kaykit_character.py`: the script drops
the pack's animation clips and static weapon meshes, merges the skinned body parts into the single
skinned mesh the engine's mesh cook imports, and extracts the character's gradient atlas. The
character's skeleton, its inverse bind matrices, and the texture are unmodified.
