using System.Text.Json;
using SqlSugar;

namespace Armory.Data;

internal class InventoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Database _database;

    public InventoryRepository(Database database)
    {
        _database = database;
    }

    private SqlSugarScope Db => _database.Client;

    // mutable classes so SqlSugar maps columns leniently by name
    private class WeaponSkinRow
    {
        public int     item_def     { get; set; }
        public int     paint_id     { get; set; }
        public float   wear         { get; set; }
        public int     seed         { get; set; }
        public int?    stattrak     { get; set; }
        public string? name_tag     { get; set; }
        public string? stickers     { get; set; }
        public string? keychain     { get; set; }
        public string? custom_model { get; set; }
    }

    private class LoadoutRow
    {
        public int    team     { get; set; }
        public string slot     { get; set; } = string.Empty;
        public int    item_def { get; set; }
    }

    private class PlayerModelRow
    {
        public int    team       { get; set; }
        public string model_path { get; set; } = string.Empty;
    }

    public async Task<Inventory> GetInventory(ulong steamId)
    {
        var parameters = new { steamId };

        var weaponRows = await Db.Ado.SqlQueryAsync<WeaponSkinRow>(
                             "SELECT item_def, paint_id, wear, seed, stattrak, name_tag, stickers, keychain, custom_model " +
                             "FROM weapon_skins WHERE steam_id = @steamId", parameters);

        var loadoutRows = await Db.Ado.SqlQueryAsync<LoadoutRow>(
                              "SELECT team, slot, item_def FROM loadouts WHERE steam_id = @steamId", parameters);

        var playerModelRows = await Db.Ado.SqlQueryAsync<PlayerModelRow>(
                                  "SELECT team, model_path FROM player_models WHERE steam_id = @steamId", parameters);

        var weapons = new Dictionary<int, WeaponSkinInfo>();

        foreach (var row in weaponRows)
        {
            weapons[row.item_def] = new WeaponSkinInfo
            {
                ItemDef     = row.item_def,
                PaintId     = (ushort) row.paint_id,
                Wear        = row.wear,
                Seed        = row.seed,
                StatTrak    = row.stattrak,
                NameTag     = row.name_tag ?? string.Empty,
                CustomModel = row.custom_model,
                Stickers    = ParseJson<StickerInfo[]>(row.stickers) ?? [],
                Keychain    = ParseJson<KeychainInfo>(row.keychain),
            };
        }

        var loadout = new Dictionary<(int, CosmeticSlot), int>();

        foreach (var row in loadoutRows)
        {
            if (Enum.TryParse<CosmeticSlot>(row.slot, true, out var slot))
            {
                loadout[(row.team, slot)] = row.item_def;
            }
        }

        return new Inventory
        {
            Weapons      = weapons,
            Loadout      = loadout,
            PlayerModels = playerModelRows.ToDictionary(r => r.team, r => r.model_path),
        };
    }

    public Task UpdateStatTrak(ulong steamId, int itemDef, int statTrak)
        => Db.Ado.ExecuteCommandAsync(
            "UPDATE weapon_skins SET stattrak = @statTrak WHERE steam_id = @steamId AND item_def = @itemDef",
            new { steamId, itemDef, statTrak });

    /// <summary>Every model path the server may ever SetModel — used to build the precache set.</summary>
    public async Task<HashSet<string>> GetAllModelPaths()
    {
        var paths = await Db.Ado.SqlQueryAsync<string>("""
                                                       SELECT model_path FROM precache_models
                                                       UNION SELECT model_path FROM player_models
                                                       UNION SELECT custom_model FROM weapon_skins WHERE custom_model IS NOT NULL
                                                       """);

        return paths.Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static T? ParseJson<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
