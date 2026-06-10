using Armory.Data;
using Armory.Services;
using Sharp.Shared.Enums;
using Sharp.Shared.HookParams;

namespace Armory.Modules;

internal class MusicKits : IArmoryService
{
    private readonly InterfaceBridge _bridge;
    private readonly IPlayerCache    _cache;

    public MusicKits(InterfaceBridge bridge, IPlayerCache cache)
    {
        _bridge = bridge;
        _cache  = cache;
    }

    public bool Init()
    {
        _bridge.HookManager.PlayerSpawnPost.InstallForward(OnPlayerSpawnPost);

        return true;
    }

    public void Shutdown()
    {
        _bridge.HookManager.PlayerSpawnPost.RemoveForward(OnPlayerSpawnPost);
    }

    private void OnPlayerSpawnPost(IPlayerSpawnForwardParams @params)
    {
        var client = @params.Client;

        if (client.IsFakeClient)
        {
            return;
        }

        if (_cache.GetLoadoutItem(client, @params.Pawn.Team, CosmeticSlot.Music) is not { } musicDef
            || _bridge.EconItemManager.GetEconItemDefinitionByIndex((EconItemId) musicDef)
                is not { DefaultLoadoutSlot: 55 })
        {
            return;
        }

        if (@params.Controller.GetInventoryService() is { } inventory)
        {
            inventory.MusicId = (ushort) musicDef;
        }
    }
}
