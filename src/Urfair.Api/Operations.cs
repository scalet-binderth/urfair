using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Urfair.Core;

namespace Urfair.Api;

public record ClientErrorRequest(string? Message, string? Path);

/// <summary>
/// Finding errors in operation: the running version, an error number for unexpected server errors
/// (shown to the user and written to the log) and browser errors in the server log. No personal data is logged.
/// </summary>
public static partial class Operations
{
    private const int MaxClientErrors = 20; // per address and 10 minutes

    /// <summary>Short commit of the running build (Render sets RENDER_GIT_COMMIT), or "dev".</summary>
    public static string Version { get; } = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") is { Length: >= 7 } c ? c[..7] : "dev";

    /// <summary>Unexpected errors get a short number: logged with path and version, returned as error "server".</summary>
    public static void UseErrorNumbers(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Urfair.Errors");
        app.Use(async (http, next) =>
        {
            try { await next(http); }
            // Bad requests (e.g. invalid JSON) stay 400 answers of the framework.
            catch (Exception e) when (e is not BadHttpRequestException && !http.RequestAborted.IsCancellationRequested)
            {
                var number = Guid.NewGuid().ToString("N")[..8];
                logger.LogError(e, "Error {Number}: {Method} {Path} (version {Version})", number, http.Request.Method, Redact(http.Request.Path), Version);
                if (http.Response.HasStarted) throw;
                http.Response.Clear();
                http.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await http.Response.WriteAsJsonAsync(new ErrorsDto([new ValidationError("server", new Dictionary<string, object> { ["number"] = number })]));
            }
        });
    }

    public static void MapOperations(this RouteGroupBuilder api)
    {
        // Errors in the browser (sent by the web app), so they show up in the server log as well.
        api.MapPost("/client-error", (ClientErrorRequest req, HttpContext http, IMemoryCache cache, ILoggerFactory loggers) =>
        {
            var key = "client-error:" + http.Connection.RemoteIpAddress;
            var count = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10); return 0; }) + 1;
            cache.Set(key, count, TimeSpan.FromMinutes(10));
            if (count <= MaxClientErrors)
                loggers.CreateLogger("Urfair.Browser").LogWarning("Browser error at {Path} (version {Version}): {Message}",
                    Redact(Cut(req.Path, 200)), Version, Cut(req.Message, 1000));
            return Results.NoContent();
        });
    }

    /// <summary>Personal links are secrets: /p/{token} is logged without the token.</summary>
    public static string Redact(string? path) => TokenPath().Replace(path ?? "", "/p/…");

    static string Cut(string? text, int max) => text is null ? "" : text.Length <= max ? text : text[..max] + "…";

    [GeneratedRegex(@"/p/[^/?#]+")]
    private static partial Regex TokenPath();
}
