using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Urfair.Api.Data;
using Urfair.Core;

namespace Urfair.Api;

public record AccountDto(Guid Id, string Email, string Name, bool IsJuniaAdmin, IReadOnlyList<string> Parishes, bool IsDemo);
public record AccountListDto(Guid Id, string Email, string Name, bool IsJuniaAdmin, IReadOnlyList<string> Parishes, bool HasPassword, string? SetupToken);
public record LoginRequest(string? Email, string? Password);
public record SetupRequest(string? Key, string? Email, string? Name, string? Password);
public record PasswordRequest(string? Password);
public record AccountRequest(string? Email, string? Name, bool IsJuniaAdmin, List<string>? Parishes);
public record DemoLoginRequest(string? Key);

/// <summary>
/// Organiser accounts: sign-in with e-mail and password (cookie), one-time links to set a password,
/// the very first Junia admin via Admin:Key, and button sign-in for demo accounts.
/// </summary>
public static class AuthEndpoints
{
    private const int MaxFailedLogins = 10;
    private static readonly TimeSpan SetupLinkLifetime = TimeSpan.FromDays(7);

    /// <summary>Demo accounts (demo mode only): key, e-mail, name, Junia admin, Pfarrgemeinden.</summary>
    public static readonly (string Key, string Email, string Name, bool Junia, string[] Parishes)[] DemoAccounts =
    [
        ("junia", "junia@demo.urfair", "Junia-Team (Demo)", true, []),
        ("markus", "markus@demo.urfair", "Admin St. Markus (Demo)", false, ["Pfarrgemeinde Urfahr-St. Markus"]),
        ("geist", "geist@demo.urfair", "Admin Hl. Geist (Demo)", false, ["Pfarrgemeinde Urfahr-Hl. Geist"]),
    ];

    public static void AddAppAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "urfair.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromHours(12);
            o.SlidingExpiration = true;
            // An API: answer with status codes instead of redirecting to a login page.
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorization();
    }

    public static void MapAuthApi(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/me", async (HttpContext http, AppDbContext db, IConfiguration cfg) =>
        {
            var a = await Access.CurrentAsync(http, db);
            var setupNeeded = !Database.IsDemo(cfg) && !await db.Accounts.AnyAsync(x => !x.IsDemo);
            return new { account = a is null ? null : ToDto(a), setupNeeded };
        });

        auth.MapPost("/login", async (LoginRequest req, HttpContext http, AppDbContext db, IMemoryCache cache) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            var key = "login:" + email;
            var fails = cache.Get<int>(key);
            if (fails >= MaxFailedLogins) return Endpoints.Errors("loginLocked");
            var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email && !x.IsDemo);
            if (a is null || !Passwords.Verify(req.Password ?? "", a.PasswordHash))
            {
                cache.Set(key, fails + 1, TimeSpan.FromMinutes(15));
                return Endpoints.Errors("loginFailed");
            }
            cache.Remove(key);
            await SignInAsync(http, a);
            return Results.Ok(ToDto(a));
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        // Demo mode: sign in as one of the demo accounts with a button.
        auth.MapPost("/demo", async (DemoLoginRequest req, HttpContext http, AppDbContext db, IConfiguration cfg) =>
        {
            if (!Database.IsDemo(cfg)) return Results.NotFound();
            var demo = DemoAccounts.FirstOrDefault(d => d.Key == req.Key);
            if (demo.Key is null) return Results.NotFound();
            var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Email == demo.Email);
            if (a is null) return Results.NotFound();
            await SignInAsync(http, a);
            return Results.Ok(ToDto(a));
        });

        // The very first Junia admin of a real instance: only while there is no account, with Admin:Key.
        auth.MapPost("/setup", async (SetupRequest req, HttpContext http, AppDbContext db, IConfiguration cfg) =>
        {
            var key = cfg["Admin:Key"];
            if (Database.IsDemo(cfg) || string.IsNullOrEmpty(key) || await db.Accounts.AnyAsync(x => !x.IsDemo)) return Results.NotFound();
            if (req.Key != key) return Endpoints.Errors("unauthorized");
            if (CheckAccount(req.Email, req.Name, out var email, out var name) is { } error) return error;
            if (CheckPassword(req.Password) is { } weak) return weak;
            var a = new AccountEntity { Email = email, Name = name, IsJuniaAdmin = true, PasswordHash = Passwords.Hash(req.Password!) };
            db.Accounts.Add(a);
            await db.SaveChangesAsync();
            await SignInAsync(http, a);
            return Results.Ok(ToDto(a));
        });

        // One-time link to set (or reset) the password.
        auth.MapGet("/password/{token}", async (string token, AppDbContext db) =>
            await FindSetupAsync(db, token) is { } a ? Results.Ok(new { a.Name, a.Email }) : Results.NotFound());

        auth.MapPost("/password/{token}", async (string token, PasswordRequest req, HttpContext http, AppDbContext db) =>
        {
            var a = await FindSetupAsync(db, token);
            if (a is null) return Results.NotFound();
            if (CheckPassword(req.Password) is { } weak) return weak;
            (a.PasswordHash, a.SetupToken, a.SetupTokenExpires) = (Passwords.Hash(req.Password!), null, null);
            await db.SaveChangesAsync();
            await SignInAsync(http, a);
            return Results.Ok(ToDto(a));
        });
    }

    /// <summary>Account management for Junia admins (inside /api/admin, so the permission filter applies).</summary>
    public static void MapAccountAdmin(this RouteGroupBuilder admin)
    {
        admin.MapGet("/accounts", async (AppDbContext db) =>
            (await db.Accounts.AsNoTracking().Where(a => !a.IsDemo).OrderBy(a => a.Name).ToListAsync())
                .Select(a => new AccountListDto(a.Id, a.Email, a.Name, a.IsJuniaAdmin, a.Parishes, a.PasswordHash is not null,
                    a.SetupTokenExpires > DateTime.UtcNow ? a.SetupToken : null)));

        admin.MapPost("/accounts", async (AccountRequest req, AppDbContext db) =>
        {
            if (CheckAccount(req.Email, req.Name, out var email, out var name) is { } error) return error;
            if (CheckParishes(req, out var parishes) is { } bad) return bad;
            if (await db.Accounts.AnyAsync(a => a.Email == email)) return Endpoints.Errors("accountExists");
            var a = new AccountEntity { Email = email, Name = name, IsJuniaAdmin = req.IsJuniaAdmin, Parishes = parishes };
            NewSetupLink(a);
            db.Accounts.Add(a);
            await db.SaveChangesAsync();
            return Results.Ok(new { a.Id, a.SetupToken });
        });

        admin.MapPut("/accounts/{accountId:guid}", async (Guid accountId, AccountRequest req, HttpContext http, AppDbContext db) =>
        {
            var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId && !x.IsDemo);
            if (a is null) return Results.NotFound();
            if (CheckAccount(a.Email, req.Name, out _, out var name) is { } error) return error;
            if (CheckParishes(req, out var parishes) is { } bad) return bad;
            if (a.Id == Access.Account(http).Id && !req.IsJuniaAdmin) return Endpoints.Errors("ownJuniaRole");
            (a.Name, a.IsJuniaAdmin, a.Parishes) = (name, req.IsJuniaAdmin, parishes);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // New one-time link, e.g. for a forgotten password. The current password stays valid until a new one is set.
        admin.MapPost("/accounts/{accountId:guid}/setup-link", async (Guid accountId, AppDbContext db) =>
        {
            var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId && !x.IsDemo);
            if (a is null) return Results.NotFound();
            NewSetupLink(a);
            await db.SaveChangesAsync();
            return Results.Ok(new { a.SetupToken });
        });

        admin.MapDelete("/accounts/{accountId:guid}", async (Guid accountId, HttpContext http, AppDbContext db) =>
        {
            if (accountId == Access.Account(http).Id) return Endpoints.Errors("ownAccount");
            var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId && !x.IsDemo);
            if (a is null) return Results.NotFound();
            db.Accounts.Remove(a);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>Creates the demo accounts if missing (demo mode).</summary>
    public static async Task SeedDemoAccountsAsync(AppDbContext db)
    {
        foreach (var d in DemoAccounts)
            if (!await db.Accounts.AnyAsync(a => a.Email == d.Email))
                db.Accounts.Add(new AccountEntity { Email = d.Email, Name = d.Name, IsJuniaAdmin = d.Junia, Parishes = [.. d.Parishes], IsDemo = true });
        await db.SaveChangesAsync();
    }

    public static AccountDto ToDto(AccountEntity a) => new(a.Id, a.Email, a.Name, a.IsJuniaAdmin, a.Parishes, a.IsDemo);

    private static Task SignInAsync(HttpContext http, AccountEntity a) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, a.Id.ToString()), new Claim(ClaimTypes.Name, a.Name)],
            CookieAuthenticationDefaults.AuthenticationScheme)));

    private static void NewSetupLink(AccountEntity a)
    {
        a.SetupToken = ParticipantEntity.NewToken();
        a.SetupTokenExpires = DateTime.UtcNow + SetupLinkLifetime;
    }

    private static Task<AccountEntity?> FindSetupAsync(AppDbContext db, string token) =>
        db.Accounts.FirstOrDefaultAsync(a => a.SetupToken == token && token != "" && a.SetupTokenExpires > DateTime.UtcNow && !a.IsDemo);

    private static IResult? CheckAccount(string? emailText, string? nameText, out string email, out string name)
    {
        email = (emailText ?? "").Trim().ToLowerInvariant();
        name = (nameText ?? "").Trim();
        if (name.Length == 0) return Endpoints.Errors("nameRequired");
        if (name.Length > Validation.MaxShortText) return Endpoints.Errors("textTooLong");
        return ParticipantCsv.IsEmail(email) ? null : Endpoints.Errors("emailInvalid");
    }

    private static IResult? CheckParishes(AccountRequest req, out List<string> parishes)
    {
        parishes = [];
        foreach (var p in req.Parishes ?? [])
        {
            if (Parishes.Match(p) is not { } official)
                return Results.BadRequest(new ErrorsDto([new("parishUnknown", new Dictionary<string, object> { ["value"] = p })]));
            if (!parishes.Contains(official)) parishes.Add(official);
        }
        return req.IsJuniaAdmin || parishes.Count > 0 ? null : Endpoints.Errors("parishRequired");
    }

    private static IResult? CheckPassword(string? password) =>
        (password ?? "").Length >= Passwords.MinLength
            ? null
            : Results.BadRequest(new ErrorsDto([new("passwordTooShort", new Dictionary<string, object> { ["min"] = Passwords.MinLength })]));
}
