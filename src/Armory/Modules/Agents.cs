using Armory.Data;
using Armory.Services;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.HookParams;
using Sharp.Shared.Types.Tier;

namespace Armory.Modules;

internal class Agents : IArmoryService
{
    // CCSWeaponBaseVData-relative offset of the VO prefix string in the agent's econ definition
    private const int VoPrefixOffset = 0x3A8;

    private readonly InterfaceBridge _bridge;
    private readonly IPlayerCache    _cache;
    private readonly ILogger<Agents> _logger;

    public Agents(InterfaceBridge bridge, IPlayerCache cache, ILogger<Agents> logger)
    {
        _bridge = bridge;
        _cache  = cache;
        _logger = logger;
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

    private unsafe void OnPlayerSpawnPost(IPlayerSpawnForwardParams @params)
    {
        var client = @params.Client;

        if (client.IsFakeClient)
        {
            return;
        }

        var pawn = @params.Pawn;

        if (_cache.GetLoadoutItem(client, pawn.Team, CosmeticSlot.Agent) is not { } agentDef
            || _bridge.EconItemManager.GetEconItemDefinitionByIndex((EconItemId) agentDef)
                is not { DefaultLoadoutSlot: 38 } def)
        {
            return;
        }

        pawn.SetNetVar("m_nCharacterDefIndex", (ushort) agentDef);
        pawn.SetModel(def.BaseDisplayModel);

        var voPrefix = new CUtlString(*(byte**) (def.GetAbsPtr() + VoPrefixOffset));
        var span     = voPrefix.AsSpan();

        var hasFemaleVoice = !span.IsEmpty
                             && (span.IndexOf("_fem"u8) >= 0
                                 || span.SequenceEqual("fbihrt_epic"u8)
                                 || span.SequenceEqual("swat_epic"u8));

        pawn.SetNetVar("m_bHasFemaleVoice", hasFemaleVoice);
        pawn.SetNetVarUtlString("m_strVOPrefix", voPrefix.Get());
    }
}
