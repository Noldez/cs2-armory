using System.Text.Json;
using Dapper;

namespace Armory.Data;

internal class InventoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Database _database;

    public InventoryRepository(Database database)
    {
        _database = database;
    }

    private record WeaponSkinRow(
        long    item_def,
        ushort  paint_id,
        float   wear,
        int     seed,
        int?    stattrak,
        string? name_tag,
        string? stickers,
        string? keychain,
        string? custom_model);

    private record LoadoutRow(int team, string slot, int item_def);

    public async Task<Inventory> GetInventory(ulong steamId)
    {
        await using var db = _database.Open();

        var weaponRows = await db.QueryAsync<WeaponSkinRow>(
                             "SELECT item_def, paint_id, wear, seed, stattrak, name_tag, stickers, keychain, custom_model " +
                             "FROM weapon_skins WHERE steam_id = @steamId", new { steamId });

        var loadoutRows = await db.QueryAsync<LoadoutRow>(
                              "SELECT team, slot, item_def FROM loadouts WHERE steam_id = @steamId", new { steamId });

        var playerModelRows = await db.QueryAsync<(int team, string model_path)>(
                                  "SELECT team, model_path FROM player_models WHERE steam_id = @steamId", new { steamId });

        var weapons = new Dictionary<int, WeaponSkinInfo>();

        foreach (var row in weaponRows)
        {
            weapons[(int) row.item_def] = new WeaponSkinInfo
            {
                ItemDef     = (int) row.item_def,
                PaintId     = row.paint_id,
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

    public async Task UpdateStatTrak(ulong steamId, int itemDef, int statTrak)
    {
        await using var db = _database.Open();

        await db.ExecuteAsync(
            "UPDATE weapon_skins SET stattrak = @statTrak WHERE steam_id = @steamId AND item_def = @itemDef",
            new { steamId, itemDef, statTrak });
    }

    /// <summary>Every model path the server may ever SetModel — used to build the precache set.</summary>
    public async Task<HashSet<string>> GetAllModelPaths()
    {
        await using var db = _database.Open();

        var paths = await db.QueryAsync<string>("""
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
