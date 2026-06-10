using System.Runtime.CompilerServices;
using Armory.Data;
using Armory.Modules;
using Armory.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Extensions.CommandManager;
using Sharp.Shared;
using Sharp.Shared.Abstractions;

[assembly: DisableRuntimeMarshalling]

namespace Armory;

public class Armory : IModSharpModule
{
    string IModSharpModule.DisplayName   => "Armory";
    string IModSharpModule.DisplayAuthor => "Noldez";

    private readonly ServiceProvider  _serviceProvider;
    private readonly ILogger<Armory>  _logger;

    public Armory(ISharedSystem  sharedSystem,
                  string         dllPath,
                  string         sharpPath,
                  Version        version,
                  IConfiguration coreConfiguration,
                  bool           hotReload)
    {
        var loggerFactory = sharedSystem.GetLoggerFactory();
        _logger = loggerFactory.CreateLogger<Armory>();

        var bridge = new InterfaceBridge(dllPath, sharpPath, version, sharedSystem);
        var config = ArmoryConfig.Load(sharpPath);

        var services = new ServiceCollection();

        services.AddSingleton(bridge);
        services.AddSingleton(config);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(sharedSystem);
        services.AddLogging();
        services.AddCommandManager(sharedSystem);

        services.AddSingleton<Database>();
        services.AddSingleton<InventoryRepository>();

        // init order = registration order
        services.AddSingleton<IArmoryService, SchemaBootstrap>();

        services.AddSingleton<ModelGuard>();
        services.AddSingleton<IModelGuard>(x => x.GetRequiredService<ModelGuard>());
        services.AddSingleton<IArmoryService>(x => x.GetRequiredService<ModelGuard>());

        services.AddSingleton<PlayerCache>();
        services.AddSingleton<IPlayerCache>(x => x.GetRequiredService<PlayerCache>());
        services.AddSingleton<IArmoryService>(x => x.GetRequiredService<PlayerCache>());

        services.AddSingleton<IArmoryService, WeaponSkins>();
        services.AddSingleton<IArmoryService, Gloves>();
        services.AddSingleton<IArmoryService, Agents>();
        services.AddSingleton<IArmoryService, MusicKits>();
        services.AddSingleton<IArmoryService, Medals>();
        services.AddSingleton<IArmoryService, PlayerModels>();
        services.AddSingleton<IArmoryService, MigrationCommand>();
        services.AddSingleton<IArmoryService, RefreshServer>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public bool Init()
    {
        foreach (var service in _serviceProvider.GetServices<IArmoryService>())
        {
            if (!service.Init())
            {
                _logger.LogError("Failed to init {service}", service.GetType().Name);

                return false;
            }

            _logger.LogInformation("{service} initialized", service.GetType().Name);
        }

        _serviceProvider.LoadAllSharpExtensions();

        return true;
    }

    public void Shutdown()
    {
        foreach (var service in _serviceProvider.GetServices<IArmoryService>().Reverse())
        {
            try
            {
                service.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shutting down {service}", service.GetType().Name);
            }
        }

        _serviceProvider.ShutdownAllSharpExtensions();
    }

    /// <summary>Creates the database/tables before anything queries them.</summary>
    private class SchemaBootstrap : IArmoryService
    {
        private readonly Database _database;

        public SchemaBootstrap(Database database) => _database = database;

        public bool Init() => _database.EnsureSchema();
    }
}
