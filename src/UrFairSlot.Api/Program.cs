using System.Text.Json.Serialization;
using UrFairSlot.Api;
using UrFairSlot.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppDatabase();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();

var app = builder.Build();

await Database.InitialiseAsync(app.Services, app.Logger);

// The Angular app is built into wwwroot; unknown paths fall back to index.html (client-side routing).
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");
app.MapApi();
app.MapAdminApi();
app.MapFallbackToFile("index.html");

app.Run();
