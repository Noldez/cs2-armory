# Armory

CS2 server-side cosmetics plugin for [ModSharp](https://github.com/Kxnrl/modsharp). Weapon skins,
knives, gloves, agents, music kits, medals, custom weapon/player models and wings — managed from a
web UI with instant in-game refresh.

Successor to the WeaponSkin-based setup, rewritten as a single module with a cleaner database and
live sync.

## Why this design

| | Old (WeaponSkin) | Armory |
|---|---|---|
| Assemblies | 3 (Core + Request.Sql + Shared) | **1 module** |
| Data access | SqlSugar code-first entities spread over 3 assemblies | SqlSugar (`SqlSugarCoreNoDrive` + MySqlConnector) with explicit SQL in one repository |
| DB tables | 9 `ws_*` tables, sticker data packed into `"0;0;0;0;0;0;0"` strings | **4 tables**, stickers/keychain as JSON columns |
| Sync | type `ws_refresh` in game | **website pushes HTTP refresh** — change applies on next spawn automatically |
| Precache | hand-edited `custom_models.json`; forgetting it = `SetModel` **crashes the server** | precache set built from the DB automatically; every `SetModel` goes through a guard that refuses un-precached models |

## Features

- **Weapon skins** — paint, wear, seed, StatTrak (persists kills), name tag, 5 sticker slots, keychain
- **Knife & gloves per team** (T / CT)
- **Agents, music kits, medals per team**
- **Custom weapon models** — replace any weapon's model with a custom `.vmdl`
- **Custom player models per team (T/CT)** — full playermodel replacement (see `docs/custom-player-models.md` for model requirements)
- **Live refresh** — localhost HTTP listener; the website calls it after every save
- **Crash-proof model swapping** — un-precached model paths are rejected and logged instead of crashing the server

## Database schema (`armory`)

```sql
weapon_skins   (steam_id, item_def) PK  -- paint_id, wear, seed, stattrak, name_tag,
                                        -- stickers JSON, keychain JSON, custom_model
loadouts       (steam_id, team, slot) PK -- slot ENUM('knife','gloves','agent','music','medal'), item_def
player_models  (steam_id, team) PK      -- model_path per team (2 = T, 3 = CT)
precache_models(model_path) PK          -- extra models to precache beyond what the tables reference
```

Tables are created automatically on first start. The precache set on each map load is the union of
`precache_models` and every model path referenced by `weapon_skins.custom_model` and `player_models`
— so anything selectable in the web UI is always safe to apply.

## Install

1. Build: `dotnet publish src/Armory -c Release`
2. Copy `publish/` contents to `game/sharp/modules/Armory/`
3. Copy `configs/armory.jsonc` to `game/sharp/configs/armory.jsonc` and fill in DB credentials + listener token
4. Start the server — schema is created automatically

## Config (`sharp/configs/armory.jsonc`)

```jsonc
{
    "Database": {
        "Host": "127.0.0.1",
        "Port": 3306,
        "Database": "armory",
        "User": "root",
        "Password": "..."
    },
    "Listener": {
        // localhost-only HTTP listener used by the website to push refreshes
        "Host": "127.0.0.1",
        "Port": 27021,
        // shared secret; the website sends it as X-Armory-Token
        "Token": "change-me"
    }
}
```

## HTTP API (listener)

| Route | Effect |
|-------|--------|
| `POST /refresh/{steamid64}` | reload that player's inventory from DB (applies on next spawn / next weapon pickup) |
| `POST /precache/reload` | re-read the precache set (new entries take effect next map load) |
| `GET /health` | `200 OK` |

All requests require header `X-Armory-Token: <token>`.

## Commands

| Command | Where | Effect |
|---------|-------|--------|
| `armory_refresh` | client | manual inventory reload |
| `armory_botmodel [model_path]` | client | set all bots' player model (testing) |
| `armory_migrate` | server console | one-time import from the old `weaponskin` database |

## Migration from WeaponSkin

Run `armory_migrate` in the server console. It copies `weaponskin.ws_*` into `armory.*`
(sticker/keychain strings are converted to JSON). The old database is not modified.

## Companion website

[cs2-armory-web](https://github.com/Noldez/cs2-armory-web) — Node.js skin-changer UI that writes
this database and pushes live refreshes.
