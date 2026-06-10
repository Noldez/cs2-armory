using Microsoft.Extensions.Configuration;

namespace Armory;

internal class DatabaseConfig
{
    public string Host     { get; set; } = "127.0.0.1";
    public int    Port     { get; set; } = 3306;
    public string Database { get; set; } = "armory";
    public string User     { get; set; } = "root";
    public string Password { get; set; } = string.Empty;

    public string ConnectionString
        => $"Server={Host};Port={Port};Database={Database};User ID={User};Password={Password};";

    // schema bootstrap needs to connect before the database exists
    public string ServerConnectionString
        => $"Server={Host};Port={Port};User ID={User};Password={Password};";
}

internal class ListenerConfig
{
    public string Host  { get; set; } = "127.0.0.1";
    public int    Port  { get; set; } = 27021;
    public string Token { get; set; } = string.Empty;
}

internal class ArmoryConfig
{
    public DatabaseConfig Database { get; set; } = new();
    public ListenerConfig Listener { get; set; } = new();

    public static ArmoryConfig Load(string sharpPath)
    {
        var configDir = Path.Combine(Path.GetFullPath(sharpPath), "configs");

        var root = new ConfigurationBuilder()
                   .SetBasePath(configDir)
                   .AddJsonFile("armory.jsonc", false, false)
                   .Build();

        var config = new ArmoryConfig();
        root.Bind(config);

        return config;
    }
}
