using System.Net;
using Microsoft.Extensions.Logging;

namespace Armory.Services;

/// <summary>
///     Minimal localhost HTTP listener the website calls to push changes into the running server:
///     POST /refresh/{steamid64}, POST /precache/reload, GET /health.
///     Every request must carry "X-Armory-Token".
/// </summary>
internal class RefreshServer : IArmoryService
{
    private readonly ListenerConfig         _config;
    private readonly IPlayerCache           _cache;
    private readonly IModelGuard            _modelGuard;
    private readonly ILogger<RefreshServer> _logger;

    private HttpListener?            _listener;
    private CancellationTokenSource? _cts;

    public RefreshServer(ArmoryConfig           config,
                         IPlayerCache           cache,
                         IModelGuard            modelGuard,
                         ILogger<RefreshServer> logger)
    {
        _config     = config.Listener;
        _cache      = cache;
        _modelGuard = modelGuard;
        _logger     = logger;
    }

    public bool Init()
    {
        if (string.IsNullOrWhiteSpace(_config.Token))
        {
            _logger.LogWarning("Listener token is empty — refresh listener disabled. Set Listener:Token in armory.jsonc");

            return true;
        }

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://{_config.Host}:{_config.Port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            _ = AcceptLoop(_cts.Token);

            _logger.LogInformation("Refresh listener on http://{host}:{port}/", _config.Host, _config.Port);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start refresh listener on {host}:{port}", _config.Host, _config.Port);

            // cosmetics still work without live refresh; don't block the whole plugin
            return true;
        }
    }

    public void Shutdown()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is { IsListening: true } listener)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested || _listener is not { IsListening: true })
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Refresh listener accept failed");

                continue;
            }

            try
            {
                Handle(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Refresh listener request failed");
                TryRespond(context, 500);
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var path    = request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;

        if (request.Headers["X-Armory-Token"] != _config.Token)
        {
            TryRespond(context, 401);

            return;
        }

        if (request.HttpMethod == "GET" && path == "/health")
        {
            TryRespond(context, 200);

            return;
        }

        if (request.HttpMethod == "POST" && path == "/precache/reload")
        {
            _modelGuard.ReloadFromDatabase();
            TryRespond(context, 202);

            return;
        }

        if (request.HttpMethod == "POST" && path.StartsWith("/refresh/")
                                         && ulong.TryParse(path["/refresh/".Length..], out var steamId))
        {
            var online = _cache.RefreshBySteamId(steamId);
            TryRespond(context, online ? 200 : 404);

            return;
        }

        TryRespond(context, 404);
    }

    private static void TryRespond(HttpListenerContext context, int status)
    {
        try
        {
            context.Response.StatusCode = status;
            context.Response.Close();
        }
        catch
        {
            // client went away — nothing to do
        }
    }
}
