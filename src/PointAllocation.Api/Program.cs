using PointAllocation.Api;
using PointAllocation.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppDatabase();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddHealthChecks();

var app = builder.Build();

await Database.InitialiseAsync(app.Services);

// The Angular app is built into wwwroot; unknown paths fall back to index.html (client-side routing).
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");
app.MapApi();
app.MapFallbackToFile("index.html");

app.Run();
