using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Urfair.Api;
using Urfair.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppDatabase();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache(); // counts failed link checks and logins
builder.Services.AddHostedService<PhaseClock>(); // registration deadline and end after the last date
builder.AddAppAuthentication();
// Keys for the sign-in cookies live in the database, so sign-ins survive restarts of the (free) server.
builder.Services.AddDataProtection().SetApplicationName("Urfair").PersistKeysToDbContext<AppDbContext>();

var app = builder.Build();

await Database.InitialiseAsync(app.Services, app.Logger);

// The Angular app is built into wwwroot; unknown paths fall back to index.html (client-side routing).
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapApi();
app.MapParticipantLinks();
app.MapAuthApi();
app.MapAdminApi();
app.MapFallbackToFile("index.html");

app.Run();
