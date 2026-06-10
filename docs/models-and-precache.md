# Custom Models & Precaching — How It Works and Why

Hard-earned knowledge from getting custom weapon/player models working on CS2. Read this
before adding a model or touching ModelGuard.

## The two iron rules of CS2 custom models

### 1. Loose files cannot override VPK content

CS2 mounts all `pak01.vpk` files **before** loose directories in the search path (check
the `Path ID:` dump at server startup — VPKs are listed first). A loose file at a stock
path like `weapons/models/knife/knife_karambit/weapon_knife_karambit.vmdl_c` is silently
ignored because pak01 already provides that path.

**Consequence:** custom models must live at paths that exist in **no** VPK
(e.g. `models/jiye/...`, `weapons/nozb1/...`, `characters/models/nozb1/...`) and be applied
with `SetModel` via Armory's `custom_model` / `player_models` mechanism. "Replacing" a stock
model by dropping a file at its path does not work (mods that claim to do this expect you
to edit pak01.vpk itself — don't; Steam validation/updates will fight you).

### 2. Precaching only happens at map load

`IModSharp.PrecacheResource()` writes into the engine's **resource manifest**
(`pContext->m_pResourceManifest->AddResource(...)` in modsharp's native code). That manifest
only exists while a map is loading — there is **no API to precache a resource mid-map**.
Calling `SetModel` with a model that was never in the manifest crashes the server.

**Consequence:** ModelGuard rebuilds the precache set from the database on every map load
(`OnResourcePrecache`) and **refuses** any `SetModel` for a path not precached on the
current map, logging a warning instead of crashing. A model added to the DB mid-map becomes
usable on the next map change.

## Making mid-map selection work anyway: catalog seeding

The website seeds `precache_models` with **every** model in its catalogs
(`custom_skins.json` + `PLAYER_MODELS_CATALOG`) at startup. ModelGuard's precache set is the
union of `precache_models` and everything referenced by `weapon_skins.custom_model` /
`player_models`, so the entire catalog is precached on every map — selecting any catalog
model mid-game applies instantly.

The only time a map change is needed: a **brand-new** model added while a map is already
running.

## Adding a new model, end to end

1. Put the compiled files (`.vmdl_c`, `.vmat_c`, `.vtex_c`) under `game/csgo/` at a path
   that exists in no VPK, with internal material references intact.
2. Add a catalog entry (`custom_skins.json` for weapons, `PLAYER_MODELS_CATALOG` in
   `server.js` for player models) with the `.vmdl` path (no `_c`).
3. Restart the website (seeds `precache_models`, pushes a precache reload to the plugin).
4. One map change → the model is precached on this and every future map.
5. Select it in the web UI — applies live from then on.

Player models must additionally satisfy the skeleton/attachment requirements in
[custom-player-models.md](custom-player-models.md).

## Unpacking `.vpkmod` files (Chinese "Source 工具箱" mods)

Format: `MOD\x01` magic, metadata, then an **LZ4 frame** (~offset 0x1A) containing:
length-prefixed mod name (UTF-8), length-prefixed author, u32-size-prefixed JPEG preview,
then an 8-byte size + an **embedded standard VPK** (magic `34 12 AA 55`).

Decode the LZ4 frame, scan for the VPK magic, dump from there, then extract with
Source 2 Viewer CLI. Files named `*.vdata_c.patch` inside are for ZombieDen's custom
loader and are useless on a stock/ModSharp server — only the model/material files matter.

### WARNING: `phase2/...` vpkmod models crash vanilla CS2 (learned 2026-06-10)

ZombieDen's loader uses a two-part "phase2" system: per-weapon **base models** at
`phase2/weapons/models/<gun>/weapon_*_ag2.vmdl` (shipped by their loader, vdata-patched
over the stock model) and skin vmdls at author paths that *reference* them — the working
Neco Deagle's RERL lists `phase2/weapons/models/deagle/weapon_pist_deagle_ag2.vmdl` plus
`animation/skeletons/weapons/deagle.vnmskel`.

Some vpkmods (e.g. the ZED Miku MP7) instead ship a **self-contained vmdl at the phase2
path itself** with embedded mesh/anim/phys but **no `NmSkeletonList` / `.vnmskel`
reference at all**. Applied via `SetModel` on a vanilla+ModSharp server, that model
**crashes both client and server** with a null-read access violation the moment the
weapon spawns (same failure class as the AK_EA first-compile post-mortem). Precaching
succeeds; the crash happens when the unified weapon anim system touches the model.

**Rule: check the RERL of every vpkmod weapon vmdl before installing**
(`Source2Viewer-CLI -i x.vmdl_c -b RERL`). No `animation/skeletons/weapons/*.vnmskel`
reference ⇒ do **not** install it as-is — rebuild it with the recipe in
[porting-csgo-weapons.md](porting-csgo-weapons.md) § "Rebuilding a broken vpkmod model".
The rebuild is cheap: these meshes are already skinned to the stock CS2 skeleton.
