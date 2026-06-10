using Armory.Services;
using Microsoft.Extensions.Logging;
using Sharp.Extensions.CommandManager;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Abstractions;
using Sharp.Shared.Enums;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;

namespace Armory.Modules;

/// <summary>
///     !shop / !guns chat command — weapon menu (via MenuManager) that gives the selected weapon.
///     Weapons go through GiveNamedItem, so skins and custom models apply like any other give.
/// </summary>
internal class Shop : IArmoryService
{
    private static readonly (string Name, EconItemId Item)[] Weapons =
    [
        ("MP7", EconItemId.Mp7),
        ("Desert Eagle", EconItemId.Deagle),
        ("AK-47", EconItemId.Ak47),
        ("M4A1-S", EconItemId.M4A1Silencer),
        ("AWP", EconItemId.Awp),
        ("MP9", EconItemId.Mp9),
        ("P90", EconItemId.P90),
    ];

    private readonly ISharedSystem   _sharedSystem;
    private readonly ICommandManager _commands;
    private readonly ILogger<Shop>   _logger;
    private readonly Menu            _menu;

    private IModSharpModuleInterface<IMenuManager>? _menuManager;

    public Shop(ISharedSystem sharedSystem, ICommandManager commands, ILogger<Shop> logger)
    {
        _sharedSystem = sharedSystem;
        _commands     = commands;
        _logger       = logger;

        var builder = Menu.Create().Title("Weapon Shop");

        foreach (var (name, item) in Weapons)
        {
            builder.Item(name, controller => GiveWeapon(controller, item));
        }

        _menu = builder.ExitItem().Build();
    }

    public bool Init()
    {
        _commands.RegisterClientCommand("shop", OnCommandShop);
        _commands.RegisterClientCommand("guns", OnCommandShop);

        return true;
    }

    private void OnCommandShop(IGameClient client, StringCommand command)
    {
        if (ResolveMenuManager() is not { } menuManager)
        {
            client.Print(HudPrintChannel.Chat, " [Armory] Shop unavailable — MenuManager is not loaded.");

            return;
        }

        menuManager.DisplayMenu(client, _menu);
    }

    private static void GiveWeapon(IMenuController controller, EconItemId item)
    {
        if (controller.Client.GetPlayerController()?.GetPlayerPawn() is not { IsAlive: true } pawn)
        {
            controller.Client.Print(HudPrintChannel.Chat, " [Armory] You must be alive to buy weapons.");
            controller.Exit();

            return;
        }

        if (pawn.GiveNamedItem(item) is null)
        {
            controller.Client.Print(HudPrintChannel.Chat, " [Armory] Could not give that weapon.");
        }

        controller.Exit();
    }

    private IMenuManager? ResolveMenuManager()
    {
        if (_menuManager?.Instance is { } existing)
        {
            return existing;
        }

        _menuManager = _sharedSystem.GetSharpModuleManager()
                                    .GetOptionalSharpModuleInterface<IMenuManager>(IMenuManager.Identity);

        if (_menuManager?.Instance is null)
        {
            _logger.LogWarning("MenuManager not found — !shop will be unavailable");
        }

        return _menuManager?.Instance;
    }
}
