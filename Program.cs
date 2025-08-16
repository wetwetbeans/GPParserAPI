using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Model;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json;
using HtmlAgilityPack;
using System.Net.Http;
using System.Collections.Concurrent;

// Models + helpers live in Models.cs in same folder
using GuitarTabApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Allow large file uploads
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

// CORS
var allowAll = "AllowAll";
builder.Services.AddCors(o => o.AddPolicy(allowAll,
    p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// JSON: force snake_case globally
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
    options.JsonSerializerOptions.DictionaryKeyPolicy = new SnakeCaseNamingPolicy();
});

var apiKey = builder.Configuration["API_KEY"];
var app = builder.Build();
app.UseCors(allowAll);

// =====================================
// Health check
// =====================================
app.MapGet("/", () => Results.Json(new
{
    ok = true,
    service = "AlphaTab GP parser",
    formats = "GP3–GP8"
}));

// =====================================
// Example GProTab search endpoint
// (trimmed for brevity, keep yours here)
// =====================================
ConcurrentDictionary<string, (DateTime expires, List<object> results)> gproCache = new();

app.MapGet("/gprosearch", async (string q, string type) =>
{
    if (string.IsNullOrWhiteSpace(q) || string.IsNullOrWhiteSpace(type))
        return Results.BadRequest(new { error = "Missing query (q) or type parameter" });

    string searchUrl = $"https://gprotab.net/en/search?type={type}&q={Uri.EscapeDataString(q)}";
    var results = new List<object>();

    using var client = new HttpClient();
    var html = await client.GetStringAsync(searchUrl);

    var doc = new HtmlDocument();
    doc.LoadHtml(html);

    var nodes = doc.DocumentNode.SelectNodes("//ol[@class='artists']/li");
    if (nodes != null)
    {
        foreach (var node in nodes)
        {
            var title = node.SelectSingleNode(".//a")?.InnerText.Trim();
            results.Add(new { title });
        }
    }

    return Results.Json(results);
});

// =====================================
// GP file parsing endpoint
// =====================================
app.MapPost("/parse", async (HttpRequest req) =>
{
    if (!string.IsNullOrEmpty(apiKey))
    {
        if (!req.Headers.TryGetValue("x-api-key", out var key) || key != apiKey)
            return Results.Unauthorized();
    }

    if (!req.HasFormContentType)
        return Results.BadRequest("multipart/form-data required");

    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file == null || file.Length == 0)
        return Results.BadRequest("Upload .gp3/.gp4/.gp5/.gpx/.gp as 'file'");

    byte[] data;
    using (var ms = new MemoryStream())
    {
        await file.CopyToAsync(ms);
        data = ms.ToArray();
    }

    Score score;
    try
    {
        score = ScoreLoader.LoadScoreFromBytes(data, new Settings());
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"alphaTab failed to parse: {ex.Message}");
    }

    if (score.Tracks == null || score.Tracks.Count == 0)
        return Results.BadRequest("No tracks found.");

    var scoreJson = ModelsBuilder.BuildScoreJson(score);

    return Results.Json(scoreJson);
});

app.Run();
