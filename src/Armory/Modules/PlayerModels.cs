using Armory.Services;
using Microsoft.Extensions.Logging;
using Sharp.Extensions.CommandManager;
using Sharp.Shared.GameEntities;
using Sharp.Shared.HookParams;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace Armory.Modules;

/// <summary>
///     Full player model replacement. Applied on spawn and re-applied via PostThink because the
///     game resets the pawn's model when the inventory/agent system runs after spawn.
/// </summary>
internal class PlayerModels : IArmoryService
{
    private readonly InterfaceBridge       _bridge;
    private readonly IPlayerCache          _cache;
    private readonly IModelGuard           _modelGuard;
    private readonly ICommandManager       _commands;
    private readonly ILogger<PlayerModels> _logger;

    private readonly string?[]      _appliedModels = new string?[PlayerSlot.MaxPlayerCount];
    private readonly IPlayerPawn?[] _botPawns      = new IPlayerPawn?[PlayerSlot.MaxPlayerCount];

    private string? _botModel;

    public PlayerModels(InterfaceBridge       bridge,
                        IPlayerCache          cache,
                        IModelGuard           modelGuard,
                        ICommandManager       commands,
                        ILogger<PlayerModels> logger)
    {
        _bridge     = bridge;
        _cache      = cache;
        _modelGuard = modelGuard;
        _commands   = commands;
        _logger     = logger;
    }

    public bool Init()
    {
        _bridge.HookManager.PlayerSpawnPost.InstallForward(OnPlayerSpawnPost);
        _bridge.HookManager.PlayerPostThink.InstallForward(OnPlayerPostThink);
        _commands.RegisterClientCommand("armory_botmodel", OnCommandBotModel);

        return true;
    }

    public void Shutdown()
    {
        _bridge.HookManager.PlayerSpawnPost.RemoveForward(OnPlayerSpawnPost);
        _bridge.HookManager.PlayerPostThink.RemoveForward(OnPlayerPostThink);
    }

    private void OnPlayerSpawnPost(IPlayerSpawnForwardParams @params)
    {
        var client = @params.Client;
        var pawn   = @params.Pawn;

        if (client.IsFakeClient)
        {
            if (pawn is not { IsValidEntity: true })
            {
                return;
            }

            _botPawns[(int) client.Slot] = pawn;

            if (_botModel is not null)
            {
                _modelGuard.TrySetModel(pawn, _botModel);
            }

            return;
        }

        // reset so PostThink re-applies after the inventory/agent system overwrites the model
        _appliedModels[client.Slot] = null;

        if (pawn is not { IsValidEntity: true })
        {
            return;
        }

        if (_cache.Get(client).PlayerModels.TryGetValue((int) pawn.Team, out var model))
        {
            _modelGuard.TrySetModel(pawn, model);
        }
    }

    private void OnPlayerPostThink(IPlayerThinkForwardParams @params)
    {
        var client = @params.Client;

        if (client.IsFakeClient)
        {
            return;
        }

        if (@params.Pawn is not { IsValidEntity: true } pawn)
        {
            return;
        }

        var slot = (int) client.Slot;

        _cache.Get(client).PlayerModels.TryGetValue((int) pawn.Team, out var model);

        if (model == _appliedModels[slot])
        {
            return;
        }

        _appliedModels[slot] = model;

        if (model is not null)
        {
            _modelGuard.TrySetModel(pawn, model);
        }
    }

    private void OnCommandBotModel(IGameClient client, StringCommand command)
    {
        if (command.ArgCount < 2)
        {
            _logger.LogInformation("armory_botmodel <model_path> — current: {model}", _botModel ?? "none");

            return;
        }

        var model = command.GetArg(1);

        if (!_modelGuard.IsSafe(model))
        {
            _logger.LogWarning("armory_botmodel: '{model}' is not precached, refusing", model);

            return;
        }

        _botModel = model;

        var count = 0;

        foreach (var bot in _bridge.ClientManager.GetGameClientList(true))
        {
            if (!bot.IsFakeClient)
            {
                continue;
            }

            if (_botPawns[(int) bot.Slot] is { IsValidEntity: true } pawn && _modelGuard.TrySetModel(pawn, model))
            {
                count++;
            }
        }

        _logger.LogInformation("armory_botmodel: applied '{model}' to {count} bot(s)", model, count);
    }
}
