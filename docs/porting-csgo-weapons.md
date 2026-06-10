# Porting CS:GO (Source 1) Weapon Mods to CS2

How the AK-47 "Earl Awakening" (`AK_EA`, CFM mod "AK47 伯爵觉醒") was ported from CS:GO
`.mdl`/`.vtf`/`.vmt` files to a working CS2 custom weapon model. Everything here was done
with tools that ship with CS2 (`game/bin/win64/`) plus Source 2 Viewer CLI and a Python
script — no Blender modeling, no third-party SDKs.

Read [models-and-precache.md](models-and-precache.md) first for the two iron rules
(VPK-absent paths, map-load-only precache). This doc is only about producing the
compiled files.

## What a CS2 custom weapon model actually needs

Reverse-engineered from the working `ramen_m4a4_ag2.vmdl_c` (VRF `-b DATA` / `-b RED2`):

1. **Mesh skinned to the stock CS2 weapon skeleton.** For the AK-47 that is 7 bones,
   all identity rotation (positions from VRF dump of `weapons/models/ak47/weapon_rif_ak47.vmdl_c`):

   | bone | parent | local position |
   |------|--------|----------------|
   | `weapon` | (root) | 2.968889, -0.193065, 2.981911 |
   | `weapon_offset` | weapon | 0, 0, 0 |
   | `bolt` | weapon_offset | 5.735881, 0.193063, -0.376563 |
   | `clip` | weapon_offset | 3.956338, 0.193051, -4.739345 |
   | `cliprelease` | weapon_offset | 1.586032, 0.193056, -2.82377 |
   | `trigger` | weapon_offset | -0.693463, 0.193058, -2.041336 |
   | `ag1_hand_r` | weapon_offset | -3.411253, 0.193059, -4.507503 |

2. **`NmSkeletonList` → `NmSkeletonReference "animation/skeletons/weapons/ak47.vnmskel"`** —
   view/world animations come from the engine's animgraph2 system via this skeleton,
   not from the model. No `AnimGraph2List` needed for weapons.
3. **An animation block.** The model must contain at least one sequence or the server
   **crashes with an access violation** the moment the weapon spawns (see post-mortem
   below). One `EmptyAnim` node is enough.
4. **Physics bound to a bone.** `PhysicsHullFromRender` with `parent_bone = "weapon"`.
   An unbound hull (no `parent_bone`) compiles fine but is the other structural
   difference vs. ramen in the crash post-mortem.
5. **`GameDataList`** with `GenericGameData` nodes `prop_data` and `weapon_metadata`
   (copy values from the stock model's `m_keyValueText`; for the AK:
   `magazine_model = "weapons/models/ak47/weapon_rif_ak47_mag.vmdl"`,
   `fire_sequence_name = "sh_rifl_stand_firing_additive"`, `is_unified_model = true`).
6. **Attachments** copied from the stock model's MDAT block: `muzzle_flash`,
   `shell_eject`, `camera_inventory`, `weapon_holster_center`, `stattrak`, `nametag`,
   `keychain` — all parented to `weapon_offset` (quaternions converted to angles).

Template with all of this filled in:
`content/csgo/weapons/noldez/ak_ea/ak_ea.vmdl` (modeldoc28 format).

## Pipeline

### 1. Mesh: `cs_mdl_import.exe`

```
cs_mdl_import.exe -v -i <folder with models/...> -o <out> models/weapons/v_rif_ak47.mdl
```

Produces a ModelDoc `.vmdl` (CS:GO skeleton + sequences — don't use it directly) and,
the part we want, a **skinned mesh DMX** in `*_refs/mesh/*.dmx`, still in CS:GO
viewmodel space with CS:GO bone names.

### 2. Retarget the DMX (`D:\tools\ak_ea_port\retarget.py`)

```
dmxconvert.exe -i mesh.dmx -o mesh_text.dmx -oe keyvalues2
blender -b --python retarget.py -- mesh_text.dmx mesh_retargeted.dmx   (any python with numpy works)
dmxconvert.exe -i mesh_retargeted.dmx -o ak_ea_mesh.dmx -oe binary
```

The script parses the KV2 text, then:
- computes world bind positions of the CS:GO feature bones
  (`v_weapon_ak47_{bolt,clip,cliprelease,trigger}`) and solves the similarity transform
  (Umeyama) onto the CS2 bone positions above. For mods that keep stock AK proportions
  this is exact — scale 1.0, residual 0 — because CS:GO v_ space → CS2 weapon space is
  just a rotation + translation;
- transforms `position$0`, rotates `normal$0` / `tangent$0`;
- throws away the CS:GO skeleton and writes the 7 CS2 joints (jointList, bind
  baseState, DmeJoint tree);
- remaps `blendindices$0` by bone name (`*_parent`→`weapon`, bolt/clip/cliprelease/
  trigger 1:1, anything else→`weapon`), merging duplicate indices per vertex;
- rewrites `DmeMaterial.mtlName` to the destination vmat paths.

### 3. Materials: `source1import.exe`

Make a throwaway Source 1 mod dir: `s1\csgo\gameinfo.txt` (minimal GameInfo block) +
the mod's `materials/` tree, then:

```
source1import.exe -src1gameinfodir <abs path>\s1\csgo -game csgo "materials\...\*.vmt"
```

Outputs to `content\csgo_imported\`: a `.vmat` (shader `csgo_vertexlitgeneric.vfx`,
legacy phong → metalness/roughness) plus `_color/_normal/_metal/_rough.tga` decoded
from the VTFs. Exit code 255 and "Missing content for N textures" are normal — the
files are still written. Copy the TGAs (and the `*_normal.txt` sidecar:
`legacy_source1_inverted_normal 1`) to the destination materials folder and write
clean vmats referencing them.

### 4. Compile and verify

```
resourcecompiler.exe -game csgo -i content\csgo\weapons\noldez\ak_ea\ak_ea.vmdl
```

Compiles the model **and** the referenced vmats/textures in one pass. Verify with VRF:

- `-b DATA`: bone names/positions match stock exactly; `weapon_metadata` present.
- `-b RERL`: **must reference `animation/skeletons/weapons/<gun>.vnmskel`** — a weapon
  model without it crashes client+server on spawn (Miku MP7 post-mortem).
- Block list **must contain ANIM, ASEQ, AGRP and PHYS** — compare against ramen
  (`ANIM` 589 bytes there). Missing ANIM/AGRP ⇒ crash on spawn.
- `-b PHYS`: `m_boneNames = ["weapon"]`, `m_boneParents = [0]`.
- Visual: VRF glb export (`-d --gltf_export_format glb --gltf_export_materials`) +
  the Blender render script. Note VRF's glb export drops skinning unless you also pass
  `--gltf_export_animations` — fine for renders either way (which is why step 2 edits
  the DMX directly).

### 5. Install + register

Compiled output already lands in `game/csgo/weapons/noldez/ak_ea/` (VPK-absent path ✓).
Then follow "Adding a new model, end to end" in
[models-and-precache.md](models-and-precache.md): catalog entry in the website's
`custom_skins.json`, restart website, one map change.

## Crash post-mortem (2026-06-10)

First compile of `ak_ea.vmdl_c` loaded fine in VRF/Blender but **crashed server and
client with an access violation** the moment a player bought an AK with the custom
model assigned. Diff vs. the known-good ramen model showed exactly two structural
differences, both fixed in one recompile:

1. **No ANIM/ASEQ/AGRP blocks** — the model had zero animations/sequences. Fix: add
   `AnimationList` → `EmptyAnim` node (name `ref`, frame_count 1). The compile warning
   *"EmptyAnim node incompatible with AnimGraph"* is harmless — ramen's 589-byte ANIM
   block is the same thing.
2. **PHYS not bound to a bone** (`m_bonesHash = []`). Fix: `parent_bone = "weapon"`
   on the `PhysicsHullFromRender` node.

Lesson: *VRF parsing OK / Blender renders OK / resourcecompiler exit 0* does **not**
mean the engine will accept the model at runtime. Always diff the block list and PHYS
bone binding against a known-working model of the same class before testing in game.

(Also: `PhysicsHullFile` referencing the render mesh DMX silently produces **no** PHYS
block at all. Valid ModelDoc node classes can be found by grepping strings in
`game/bin/win64/tools/modeldoc_editor.dll`.)

## Rebuilding a broken vpkmod model (Miku MP7 case, 2026-06-10)

ZombieDen `phase2/...` vpkmod vmdls with no `NmSkeletonList` crash on spawn (see
[models-and-precache.md](models-and-precache.md) § vpkmod warning). Rebuilding one is a
**much shorter** version of the pipeline above because the mesh is already Source 2 and
already skinned to the stock CS2 weapon skeleton — no `cs_mdl_import`, no retargeting,
no material import:

1. **Decompile**: `Source2Viewer-CLI -i broken.vmdl_c -o <workdir> -d`. The dumped
   `*_body_legacy.dmx` keeps full skinning (`blendindices$0`/`blendweights$0`, verify
   via `dmxconvert -oe keyvalues2`) and the DmeJoint tree — it feeds `resourcecompiler`
   directly as a `RenderMeshFile`. (Contrast: VRF's **glb** export needs
   `--gltf_export_animations` or skinning is dropped; the DMX export keeps it always.)
2. **Verify the skeleton is stock**: VRF `-b DATA` on the broken model vs. the stock
   gun — bone names/positions matched exactly for the MP7, so the DMX needed zero edits.
3. **Write a fresh vmdl** from the AK_EA template: stock skeleton bones, stock MDAT
   attachments (quat→angles), `RenderMeshFile` → the dumped DMX, `EmptyAnim`,
   `PhysicsHullFromRender parent_bone="weapon"`,
   `NmSkeletonList → animation/skeletons/weapons/mp7.vnmskel`, `GameDataList` with the
   `weapon_metadata` copied from the broken model's own DATA block (it carries the
   correct holster/magazine/fire-sequence values).
4. **Materials need nothing**: the DMX's `mtlName` keeps pointing at the vpkmod's
   compiled-only `vmat_c` (here `jiye/miku_mp7/materials/miku_mp7.vmat`) — compiled-only
   material paths resolve fine at runtime (Reimu/AWP-fix precedent).
5. Compile + run the full verification checklist below, **including the RERL check**.

Template: `content/csgo/weapons/zed/miku_mp7/miku_mp7.vmdl`. Workdir: `D:\tools\miku_mp7_port\`.

### Diagnosing "is it the model?" from crash dumps

CS2 writes `cs2_*_accessviolation.mdmp` minidumps to `game/bin/win64/`.
`D:\tools\miku_mp7_port\minidump_info.py` (pure Python, runs under Blender's python:
`blender -b --factory-startup --python minidump_info.py -- <dumps...>`) prints the
exception code/address and the faulting module+offset. The malformed-model signature is a
**deterministic pair**: the same `client.dll+offset` null read on every affected client
and the same `server.dll+offset` null read on the server (same engine anim/model code
compiled into both binaries). Identical offsets across crashes hours apart = same root
cause, even across different maps/models — useful for telling "this model is broken"
apart from map or plugin crashes.

## Tool notes

- `cs_mdl_import -h` lies — run with no args for usage.
- `dmxconvert -oe keyvalues2|binary` round-trips DMX losslessly; resourcecompiler
  accepts either encoding.
- Attachment quat→angle conversion: Source convention — yaw from forward.xy, pitch
  from -forward.z, roll from left.z/up.z (see `quat2ang.py` in the port workdir).
- All intermediates preserved in `D:\tools\ak_ea_port\`.
