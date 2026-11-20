using System.Text.Json.Serialization;
using Urfair.Api;
using Urfair.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppDatabase();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache(); // counts failed link checks

var app = builder.Build();

await Database.InitialiseAsync(app.Services, app.Logger);

// The Angular app is built into wwwroot; unknown paths fall back to index.html (client-side routing).
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");
app.MapApi();
app.MapParticipantLinks();
app.MapAdminApi();
app.MapFallbackToFile("index.html");

app.Run();
