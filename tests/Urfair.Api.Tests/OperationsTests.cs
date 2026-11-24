using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Urfair.Api.Tests;

public class OperationsTests
{
    [Fact]
    public async Task Unexpected_errors_answer_with_an_error_number()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseErrorNumbers();
        app.MapGet("/boom", string () => throw new InvalidOperationException("boom"));
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0];
        Assert.Equal("server", error.GetProperty("code").GetString());
        Assert.Equal(8, error.GetProperty("args").GetProperty("number").GetString()!.Length);
    }

    [Theory]
    [InlineData("/api/p/abc123/check", "/api/p/…/check")]
    [InlineData("/p/abc123", "/p/…")]
    [InlineData("/api/slots/1/give-back", "/api/slots/1/give-back")]
    public void Personal_links_are_not_logged(string path, string logged) => Assert.Equal(logged, Operations.Redact(path));
}
