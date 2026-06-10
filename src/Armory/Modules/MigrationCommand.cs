using System.Globalization;
using System.Text.Json;
using Armory.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Sharp.Shared.Enums;
using Sharp.Shared.Types;

namespace Armory.Modules;

/// <summary>
///     One-time import from the legacy WeaponSkin database (server console: armory_migrate [sourceDb]).
///     The source database is only read, never modified.
/// </summary>
internal class MigrationCommand : IArmoryService
{
    private const string CommandName = "armory_migrate";

    private readonly InterfaceBridge           _bridge;
    private readonly ArmoryConfig              _config;
    private readonly Database                  _database;
    private readonly ILogger<MigrationCommand> _logger;

    public MigrationCommand(InterfaceBridge bridge, ArmoryConfig config, Database database, ILogger<MigrationCommand> logger)
    {
        _bridge   = bridge;
        _config   = config;
        _database = database;
        _logger   = logger;
    }

    public bool Init()
    {
        _bridge.ConVarManager.CreateServerCommand(CommandName, OnCommandMigrate);

        AutoMigrateIfEmpty();

        return true;
    }

    /// <summary>First boot convenience: if armory is empty and the legacy DB exists, import it.</summary>
    private void AutoMigrateIfEmpty()
    {
        Task.Run(async () =>
        {
            try
            {
                await using var dst = _database.Open();

                var hasData = await dst.ExecuteScalarAsync<int>(
                                  "SELECT EXISTS(SELECT 1 FROM weapon_skins) " +
                                  "OR EXISTS(SELECT 1 FROM loadouts) " +
                                  "OR EXISTS(SELECT 1 FROM player_models)");

                if (hasData != 0)
                {
                    return;
                }

                var legacyExists = await dst.ExecuteScalarAsync<int>(
                                       "SELECT COUNT(*) FROM information_schema.tables " +
                                       "WHERE table_schema = 'weaponskin' AND table_name = 'ws_weapon_cosmetics'");

                if (legacyExists == 0)
                {
                    return;
                }

                _logger.LogInformation("Armory database is empty and a legacy weaponskin database exists — importing");

                var counts  = await Migrate("weaponskin").ConfigureAwait(false);
                var summary = string.Join(", ", counts.Select(kv => $"{kv.Key}: {kv.Value}"));

                _logger.LogInformation("Auto-migration complete — {summary}", summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-migration failed (run armory_migrate manually)");
            }
        });
    }

    public void Shutdown()
    {
        _bridge.ConVarManager.ReleaseCommand(CommandName);
    }

    private ECommandAction OnCommandMigrate(StringCommand command)
    {
        var sourceDb = command.ArgCount > 1 ? command.GetArg(1) : "weaponskin";

        Task.Run(async () =>
        {
            try
            {
                var counts = await Migrate(sourceDb).ConfigureAwait(false);
                var summary = string.Join(", ", counts.Select(kv => $"{kv.Key}: {kv.Value}"));

                _logger.LogInformation("Migration from '{db}' complete — {summary}", sourceDb, summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration from '{db}' failed", sourceDb);
            }
        });

        return ECommandAction.Handled;
    }

    private record LegacyCosmetics(
        ulong   SteamId,
        int     ItemId,
        ushort  PaintId,
        float   Wear,
        float   Seed,
        int?    StatTrak,
        string? NameTag,
        string  WeaponSticker0,
        string  WeaponSticker1,
        string  WeaponSticker2,
        string  WeaponSticker3,
        string  WeaponSticker4,
        string  WeaponKeychain);

    private async Task<Dictionary<string, int>> Migrate(string sourceDb)
    {
        var source = new MySqlConnectionStringBuilder(_config.Database.ServerConnectionString)
        {
            Database = sourceDb,
        }.ConnectionString;

        await using var src = new MySqlConnection(source);
        await using var dst = _database.Open();

        var counts = new Dictionary<string, int>();

        // weapon skins (sticker strings "id;schema;x;y;wear;scale;rotation" -> JSON)
        var cosmetics = (await src.QueryAsync<LegacyCosmetics>(
                            "SELECT SteamId, ItemId, PaintId, Wear, Seed, StatTrak, NameTag, " +
                            "WeaponSticker0, WeaponSticker1, WeaponSticker2, WeaponSticker3, WeaponSticker4, WeaponKeychain " +
                            "FROM ws_weapon_cosmetics")).ToArray();

        foreach (var row in cosmetics)
        {
            string[] stickerColumns =
                [row.WeaponSticker0, row.WeaponSticker1, row.WeaponSticker2, row.WeaponSticker3, row.WeaponSticker4];

            var stickers = new List<StickerInfo>();

            for (var slot = 0; slot < stickerColumns.Length; slot++)
            {
                if (ParseLegacySticker(stickerColumns[slot], slot) is { } sticker)
                {
                    stickers.Add(sticker);
                }
            }

            var keychain = ParseLegacyKeychain(row.WeaponKeychain);

            await dst.ExecuteAsync("""
                                   INSERT INTO weapon_skins
                                       (steam_id, item_def, paint_id, wear, seed, stattrak, name_tag, stickers, keychain)
                                   VALUES (@SteamId, @ItemId, @PaintId, @Wear, @Seed, @StatTrak, @NameTag, @Stickers, @Keychain)
                                   ON DUPLICATE KEY UPDATE
                                       paint_id = VALUES(paint_id), wear = VALUES(wear), seed = VALUES(seed),
                                       stattrak = VALUES(stattrak), name_tag = VALUES(name_tag),
                                       stickers = VALUES(stickers), keychain = VALUES(keychain)
                                   """,
                                   new
                                   {
                                       row.SteamId,
                                       row.ItemId,
                                       row.PaintId,
                                       row.Wear,
                                       Seed     = (int) row.Seed,
                                       row.StatTrak,
                                       NameTag  = string.IsNullOrEmpty(row.NameTag) ? null : row.NameTag,
                                       Stickers = stickers.Count > 0 ? JsonSerializer.Serialize(stickers) : null,
                                       Keychain = keychain is not null ? JsonSerializer.Serialize(keychain) : null,
                                   });
        }

        counts["weapon_skins"] = cosmetics.Length;

        // custom weapon models -> weapon_skins.custom_model
        counts["custom_weapon_models"] = await dst.ExecuteAsync($"""
            INSERT INTO weapon_skins (steam_id, item_def, custom_model)
            SELECT SteamId, ItemId, ModelPath FROM `{sourceDb}`.ws_custom_models
            ON DUPLICATE KEY UPDATE custom_model = VALUES(custom_model)
            """);

        // team loadouts
        (string Table, string Slot)[] loadoutSources =
        [
            ("ws_team_knives", "knife"),
            ("ws_team_gloves", "gloves"),
            ("ws_team_agents", "agent"),
            ("ws_team_musickits", "music"),
            ("ws_team_medals", "medal"),
        ];

        foreach (var (table, slot) in loadoutSources)
        {
            counts[slot] = await dst.ExecuteAsync($"""
                INSERT INTO loadouts (steam_id, team, slot, item_def)
                SELECT SteamId, Team, '{slot}', ItemId FROM `{sourceDb}`.{table}
                ON DUPLICATE KEY UPDATE item_def = VALUES(item_def)
                """);
        }

        // full player models (legacy rows are team-less -> apply to both T and CT)
        counts["player_models"] = await dst.ExecuteAsync($"""
            INSERT INTO player_models (steam_id, team, model_path)
            SELECT SteamId, t.team, ModelPath
            FROM `{sourceDb}`.ws_custom_player_models
            CROSS JOIN (SELECT 2 AS team UNION ALL SELECT 3) t
            ON DUPLICATE KEY UPDATE model_path = VALUES(model_path)
            """);

        return counts;
    }

    private static StickerInfo? ParseLegacySticker(string? packed, int slot)
    {
        // legacy format: "id;schema;offsetX;offsetY;wear;scale;rotation"
        if (string.IsNullOrEmpty(packed))
        {
            return null;
        }

        var parts = packed.Split(';');

        if (parts.Length < 7 || !int.TryParse(parts[0], out var id) || id <= 0)
        {
            return null;
        }

        return new StickerInfo
        {
            Slot     = slot,
            Id       = id,
            OffsetX  = ParseFloat(parts[2]),
            OffsetY  = ParseFloat(parts[3]),
            Wear     = ParseFloat(parts[4]),
            Scale    = ParseFloat(parts[5], 1f),
            Rotation = ParseFloat(parts[6]),
        };
    }

    private static KeychainInfo? ParseLegacyKeychain(string? packed)
    {
        // legacy format: "id;x;y;z;seed"
        if (string.IsNullOrEmpty(packed))
        {
            return null;
        }

        var parts = packed.Split(';');

        if (parts.Length < 5 || !int.TryParse(parts[0], out var id) || id <= 0)
        {
            return null;
        }

        return new KeychainInfo
        {
            Id   = id,
            X    = ParseFloat(parts[1]),
            Y    = ParseFloat(parts[2]),
            Z    = ParseFloat(parts[3]),
            Seed = ParseFloat(parts[4]),
        };
    }

    private static float ParseFloat(string value, float fallback = 0f)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : fallback;
}
