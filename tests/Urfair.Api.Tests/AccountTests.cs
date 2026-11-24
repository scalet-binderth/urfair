using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Urfair.Api.Tests;

/// <summary>Organiser accounts: the access link by mail and "forgot password" (AuthEndpoints.cs).</summary>
public class AccountTests
{
    const string Eva = "eva.example@pfarre.example";

    static async Task<List<(string To, string Subject, string Body)>> MailsAsync(HttpClient junia) =>
        (await junia.GetFromJsonAsync<JsonElement>("/api/admin/accounts/mails")).EnumerateArray()
            .Select(m => (m.GetProperty("to").GetString()!, m.GetProperty("subject").GetString()!, m.GetProperty("body").GetString()!)).ToList();

    static async Task<JsonElement> AccountAsync(HttpClient junia, string email) =>
        (await junia.GetFromJsonAsync<JsonElement>("/api/admin/accounts")).EnumerateArray().Single(a => a.GetProperty("email").GetString() == email);

    static async Task<(HttpClient Junia, Guid Id)> EvaWithPasswordAsync(DemoApp app)
    {
        var junia = await app.OrganiserAsync();
        var created = await junia.PostAsJsonAsync("/api/admin/accounts", new { email = Eva, name = "Eva Example", isJuniaAdmin = false, parishes = new[] { "St. Markus" } });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        (await app.CreateClient().PostAsJsonAsync($"/api/auth/password/{body.GetProperty("setupToken").GetString()}", new { password = "ein-langes-passwort" })).EnsureSuccessStatusCode();
        return (junia, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_new_account_gets_its_one_time_link_by_mail()
    {
        using var app = new DemoApp();
        var junia = await app.OrganiserAsync();

        var created = await junia.PostAsJsonAsync("/api/admin/accounts", new { email = Eva, name = "Eva Example", isJuniaAdmin = false, parishes = new[] { "St. Markus" } });

        var token = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("setupToken").GetString()!;
        var mail = Assert.Single(await MailsAsync(junia));
        Assert.Equal((Eva, "Dein Zugang zu Urfair"), (mail.To, mail.Subject));
        Assert.Contains($"/account/{token}", mail.Body);
        Assert.Contains("Pfarrgemeinde Urfahr-St. Markus", mail.Body);
    }

    [Fact]
    public async Task Forgot_password_tells_the_Junia_admins_once_and_they_send_a_new_link()
    {
        using var app = new DemoApp();
        var (junia, id) = await EvaWithPasswordAsync(app);
        var anonymous = app.CreateClient();

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/forgot", new { email = " EVA.example@pfarre.example " })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/forgot", new { email = Eva })).StatusCode);

        var request = Assert.Single(await MailsAsync(junia), m => m.Subject.Contains("bittet um ein neues Passwort"));
        Assert.Equal("junia@demo.urfair", request.To);
        Assert.Contains("/admin/accounts", request.Body);
        Assert.Equal(JsonValueKind.String, (await AccountAsync(junia, Eva)).GetProperty("resetRequestedAt").ValueKind);

        var link = await junia.PostAsync($"/api/admin/accounts/{id}/setup-link", null);
        var token = (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("setupToken").GetString()!;

        var reset = Assert.Single(await MailsAsync(junia), m => m.Subject == "Neues Passwort für Urfair");
        Assert.Equal(Eva, reset.To);
        Assert.Contains($"/account/{token}", reset.Body);
        Assert.Equal(JsonValueKind.Null, (await AccountAsync(junia, Eva)).GetProperty("resetRequestedAt").ValueKind);
        (await anonymous.PostAsJsonAsync($"/api/auth/password/{token}", new { password = "noch-ein-langes-passwort" })).EnsureSuccessStatusCode();
        (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = Eva, password = "noch-ein-langes-passwort" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_address_answers_the_same_and_mails_nobody()
    {
        using var app = new DemoApp();
        var junia = await app.OrganiserAsync();

        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/forgot", new { email = "niemand@example.org" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await MailsAsync(junia));
    }

    [Fact]
    public async Task Only_Junia_admins_see_account_mails_and_deleting_an_account_deletes_its_mails()
    {
        using var app = new DemoApp();
        var (junia, id) = await EvaWithPasswordAsync(app);
        (await app.CreateClient().PostAsJsonAsync("/api/auth/forgot", new { email = Eva })).EnsureSuccessStatusCode();
        var markus = app.CreateClient();
        (await markus.PostAsJsonAsync("/api/auth/demo", new { key = "markus" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await markus.GetAsync("/api/admin/accounts/mails")).StatusCode);
        // Routing ignores letter case, so must the permission check.
        Assert.Equal(HttpStatusCode.Forbidden, (await markus.GetAsync("/api/admin/Accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await markus.PostAsJsonAsync("/api/admin/ACCOUNTS", new { email = "x@example.org", name = "X", isJuniaAdmin = true, parishes = Array.Empty<string>() })).StatusCode);

        (await junia.DeleteAsync($"/api/admin/accounts/{id}")).EnsureSuccessStatusCode();
        Assert.Empty(await MailsAsync(junia));
    }
}
