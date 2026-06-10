# CS2 Custom Player Models — Requirements

How a custom player model must be structured to work in current CS2 (AnimGraph2 era).
Derived from fixing the Silver Wolf model (T-pose 2026-06-08, weapons-at-feet 2026-06-10)
by comparing against official agent models.

## TL;DR checklist

A working third-person player model needs ALL of these:

1. **No legacy animgraph** — remove `anim_graph_name = "..."` (old `player_ct.vanmgrph` /
   `animset_ct.vmdl` were deleted from CS2; referencing them = T-pose).
2. **NmSkeletonList** referencing the shared skeletons.
3. **AnimGraph2List** referencing the shared graphs.
4. **Skeleton bones matching `worldmodel.vnmskel` names** — especially the `wpn` chain.
5. **Attachment `weapon` parented to bone `wpn`** — without it the held weapon renders
   at the model origin (between/below the feet).
6. **`name = "default"` on `BodyGroupChoice`** (required by current resourcecompiler).
7. **Precached on the server before `SetModel`** (unprecached SetModel = instant server crash).

## 1. Animation system blocks (VMDL root node children)

```kv3
{
    _class = "NmSkeletonList"
    children =
    [
        { _class = "NmSkeletonReference" filename = "animation/skeletons/characters/worldmodel.vnmskel" },
        { _class = "NmSkeletonReference" filename = "animation/skeletons/characters/viewmodel.vnmskel" },
    ]
},
{
    _class = "AnimGraph2List"
    children =
    [
        { _class = "DefaultAnimGraph2" filename = "animation/graphs/worldmodel/worldmodel.vnmgraph" },
        { _class = "AnimGraph2" name = "uimodel"  filename = "animation/graphs/ui/uimodel.vnmgraph" },
        { _class = "AnimGraph2" name = "viewmodel" filename = "animation/graphs/viewmodel/viewmodel.vnmgraph" },
    ]
},
```

## 2. Required skeleton bones

The animation system drives bones **by name**, matching `animation/skeletons/characters/worldmodel.vnmskel`
(74 bones — dump it with VRF to see the canonical list, parents, and reference pose).
Standard humanoid bones (`root_motion`, `pelvis`, spine chain, limbs, fingers) must use those exact names.

Beyond the humanoid bones, add these helper bones (all with `do_not_discard = true`):

| Bone | Parent | Purpose |
|------|--------|---------|
| `wpnPivot` | `root_motion` | weapon chain root (ref origin ~[-3.47, 0, 58.78]) |
| `wpn` | `wpnPivot` | **held-weapon bone — the `weapon` attachment lives here** |
| `wpnHand_L` / `wpnHand_R` | `wpn` | hand IK goals (origin [0, ±6, -4]) |
| `wpnTip` / `wpnEnd` | `wpn` | origin [±6, 0, 0] |
| `attachHand_L` / `attachHand_R` | `hand_L` / `hand_R` | origin [0,0,0] |
| `attachFoot_L` / `attachFoot_R` | `ankle_L` / `ankle_R` | origin [0,0,0] |
| `wpnAimIntent` | `root_motion` | origin [0,0,0] |
| `attachWorld` | `root_motion` | origin [0,0,0] |

Notes:
- Official agents keep only `wpnPivot` + `wpn` in the render skeleton; the rest are
  harmless to include and exist in the vnmskel.
- CS:GO-era helper bones (`weapon_hand_L/R`, `weaponhier_*`, `*_iktarget`, `lh_ik_driver`)
  are **not** in the vnmskel and are never driven — don't parent anything important to them.

## 3. Required attachments (AttachmentList)

Copied from official agents (`agents/models/ctm_st6/ctm_st6_variantg.vmdl_c`):

| Attachment | parent_bone | relative_origin | relative_angles | Used for |
|------------|-------------|-----------------|-----------------|----------|
| `weapon` | `wpn` | [0,0,0] | [0,0,0] | **third-person held weapon** |
| `weapon_hand_r` | `hand_R` | [-2.6,-1.4,0] | [0,180,0] | hand effects |
| `weapon_hand_l` | `hand_L` | [2.6,1.4,0] | [0,0,180] | hand effects |
| `weapon_center` | `""` (root) | [0,0,40] | [0,0,0] | UI/effects center |
| `pistol` | `leg_upper_r` | model-specific | | holstered pistol |
| `knife` / `eholster` | `leg_upper_l` | model-specific | | holstered knife |
| `c4` / `primary_smg` | `spine_3` | model-specific | | back weapons |
| `primary` | `jiggle_primary` | model-specific | | back primary |
| `grenade0..4`, `defusekit` | `pelvis` | model-specific | | belt items |
| `clip_limit` | `head_0` | model-specific | | camera clip |

**Symptom map:** weapon floating at the model origin (between/below feet) in third person,
fine in first person → the `weapon` attachment is missing or its `wpn` bone isn't in the skeleton.

## 4. Compile & verify

```powershell
# Source lives in content/, output goes to game/ automatically:
& "game\bin\win64\resourcecompiler.exe" -game csgo -i "content\csgo\characters\models\<path>\<model>.vmdl"

# Verify with VRF (C:\tools\vrf\Source2Viewer-CLI.exe):
Source2Viewer-CLI.exe -i <model>.vmdl_c -b DATA   # skeleton bone names, NmSkeletonRefs, animGraph2Refs
Source2Viewer-CLI.exe -i <model>.vmdl_c -b MDAT   # attachments (m_name / m_influenceNames), bone parents

# Read official reference files straight out of the VPK:
Source2Viewer-CLI.exe -i game\csgo\pak01_dir.vpk --vpk_filepath agents/models/ctm_st6/ctm_st6_variantg.vmdl_c -b MDAT
Source2Viewer-CLI.exe -i game\csgo\pak01_dir.vpk --vpk_filepath animation/skeletons/characters/worldmodel.vnmskel_c -b DATA
```

Checklist after compile:
- DATA block: `m_vecNmSkeletonRefs` lists worldmodel + viewmodel vnmskel; `m_animGraph2Refs` lists the graphs.
- MDAT block: attachment `weapon` with influence `wpn`; bones `wpnPivot`→`root_motion`, `wpn`→`wpnPivot`.
- Compile warning `animset_ct.vmdl ... not in the system` is a harmless leftover; compile still succeeds.

## 5. Server-side requirements

- Model path must be precached on map load — add it to `game/sharp/configs/custom_models.json`
  (read by WeaponSkin's `CustomModelPrecache` module). Calling `pawn.SetModel()` with an
  unprecached model **crashes the server**.
- Both client and server cache compiled models — after recompiling, restart the dedicated
  server AND the client to pick up the new `.vmdl_c`.

## 6. Official reference models

- **Real** agent models: `agents/models/...` inside `game/csgo/pak01_dir.vpk`
  (e.g. `agents/models/ctm_st6/ctm_st6_variantg.vmdl_c`).
- `characters/models/ctm_*` / `tm_*` paths are **dummy stubs** (single "dummy" bone) — don't use as reference.
- Shared skeleton: `animation/skeletons/characters/worldmodel.vnmskel_c`.
- Weapon skeletons (`animation/skeletons/weapons/*.vnmskel_c`) have root bone `weapon`;
  the game places the weapon at the player's `weapon` attachment.
