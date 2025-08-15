using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Model;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using System.Linq;
using System.Reflection;
using HtmlAgilityPack;
using System.Net.Http;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Allow large file uploads
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

// CORS
var allowAll = "AllowAll";
builder.Services.AddCors(o => o.AddPolicy(allowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var apiKey = builder.Configuration["API_KEY"];
var app = builder.Build();
app.UseCors(allowAll);

// In-memory cache for search results
ConcurrentDictionary<string, (DateTime expires, List<object> results)> gproCache = new();

// Health check
app.MapGet("/", () => Results.Json(new { ok = true, service = "AlphaTab GP parser", formats = "GP3–GP8" }));

// =========================================
// GProTab search endpoint
// =========================================
app.MapGet("/gprosearch", async (string q, string type) =>
{
    if (string.IsNullOrWhiteSpace(q) || string.IsNullOrWhiteSpace(type))
        return Results.BadRequest(new { error = "Missing query (q) or type parameter" });

    string cacheKey = $"{type}:{q}".ToLowerInvariant();
    DateTime now = DateTime.UtcNow;

    // Cache check
    if (gproCache.TryGetValue(cacheKey, out var cached) && cached.expires > now)
    {
        Console.WriteLine($"[CACHE HIT] {cacheKey}");
        return Results.Json(cached.results);
    }

    string searchUrl = $"https://gprotab.net/en/search?type={type}&q={Uri.EscapeDataString(q)}";
    var results = new List<object>();

    int maxRetries = 6;
    int delayMs = 1500;

    try
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var html = await client.GetStringAsync(searchUrl);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                if (type.ToLower() == "artist")
                {
                    var artistNodes = doc.DocumentNode.SelectNodes("//ol[@class='artists']/li");
                    if (artistNodes != null)
                    {
                        foreach (var node in artistNodes)
                        {
                            var title = node.SelectSingleNode(".//a")?.InnerText.Trim();
                            var img = node.SelectSingleNode(".//img")?.GetAttributeValue("src", null);
                            var link = node.SelectSingleNode(".//a")?.GetAttributeValue("href", null);

                            results.Add(new
                            {
                                title,
                                image = img != null ? "https://gprotab.net" + img : null,
                                link = link != null ? "https://gprotab.net" + link : null
                            });
                        }
                    }
                }
                else if (type.ToLower() == "song")
                {
                    var songNodes = doc.DocumentNode.SelectNodes("//div[@class='tabs-holder']//ul[@class='tabs']//a");
                    if (songNodes != null)
                    {
                        foreach (var node in songNodes)
                        {
                            var title = node.InnerText.Trim();
                            var link = node.GetAttributeValue("href", null);
                            results.Add(new
                            {
                                title,
                                image = (string)null,
                                link = link != null ? "https://gprotab.net" + link : null
                            });
                        }
                    }
                }

                if (results.Count > 0)
                {
                    gproCache[cacheKey] = (now.AddHours(2), results);
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Retry {attempt}] Error: {ex.Message}");
                if (attempt == maxRetries)
                {
                    return Results.Json(new { error = "Search failed after multiple attempts", query = q, type });
                }
                await Task.Delay(delayMs);
                delayMs *= 2;
            }
        }

        if (results.Count == 0)
            return Results.Json(new { error = "No results found", query = q, type });

        return Results.Json(results);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Unexpected error", message = ex.Message });
    }
});

app.MapGet("/gproartist", async (string url) =>
{
    if (string.IsNullOrWhiteSpace(url))
        return Results.BadRequest(new { error = "Missing url param" });

    var results = new List<object>();

    try
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        var html = await client.GetStringAsync(url);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Example selector for song links on artist page
        var songNodes = doc.DocumentNode.SelectNodes("//ul[contains(@class,'tabs')]/li/a");

        if (songNodes != null)
        {
            foreach (var node in songNodes)
            {
                var title = node.InnerText.Trim();
                var link = node.GetAttributeValue("href", null);

                results.Add(new
                {
                    title,
                    image = (string)null, // no image for songs
                    link = link != null ? "https://gprotab.net" + link : null
                });
            }
        }

        return Results.Json(results);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Failed to scrape artist page", message = ex.Message });
    }
});






// =========================================
// GP parse endpoint
// =========================================
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

    int tpb = GuitarTabApi.Models.Helpers.GetProp(score, "TicksPerBeat", 480);
    string[] noticesArr = string.IsNullOrWhiteSpace(score.Notices ?? "") ? Array.Empty<string>() : new[] { score.Notices! };

    var scoreJson = new GuitarTabApi.Models.ScoreJson(
        score.Artist ?? "(Unknown Artist)",
        score.Title ?? "(Untitled)",
        score.Album ?? "",
        score.SubTitle ?? "",
        score.Copyright ?? "",
        score.Music ?? "",
        score.Words ?? "",
        score.Tab ?? "",
        score.Instructions ?? "",
        noticesArr,
        score.Tempo > 0 ? score.Tempo : 120.0,
        tpb,
        Array.Empty<int>(),
        Array.Empty<string>(),
        "",
        GuitarTabApi.Models.Helpers.CollectTimeSigs(score),
        GuitarTabApi.Models.Helpers.CollectKeySigs(score),
        GuitarTabApi.Models.Helpers.CollectTempos(score),
        GuitarTabApi.Models.Helpers.CollectMarkers(score),
        GuitarTabApi.Models.Helpers.CollectRepeats(score),
        score.Tracks.Select(track => new GuitarTabApi.Models.TrackJson(
            track.Name ?? "",
            GuitarTabApi.Models.Helpers.GetProp(track, "Program", 0),
            GuitarTabApi.Models.Helpers.GetProp(track, "Channel", 0),
            GuitarTabApi.Models.Helpers.GetProp(track, "IsPercussion", false),
            GuitarTabApi.Models.Helpers.GetProp(track, "Capo", 0),
            GuitarTabApi.Models.Helpers.GetProp(track, "Transpose", 0),
            GuitarTabApi.Models.Helpers.GetProp(track, "Volume", 100),
            GuitarTabApi.Models.Helpers.GetProp(track, "Pan", 0),
            track.Staves.Select(staff => new GuitarTabApi.Models.StaffJson(
                GuitarTabApi.Models.Helpers.Tunings(staff),
                GuitarTabApi.Models.Helpers.TuningText(staff),
                GuitarTabApi.Models.Helpers.TuningLetters(staff),
                staff.Bars.Select((bar, barIdx) =>
                {
                    var section = bar.MasterBar.Section;
                    var markerForBar = section != null ? new GuitarTabApi.Models.MarkerJson(section.Text ?? "", 0, barIdx) : null;
                    return new GuitarTabApi.Models.BarJson(
                        barIdx,
                        bar.MasterBar.IsRepeatStart,
                        bar.MasterBar.IsRepeatEnd,
                        (int)bar.MasterBar.RepeatCount,
                        GuitarTabApi.Models.Helpers.AlternateEndingsToArray(bar.MasterBar.AlternateEndings),
                        (int)bar.BarLineRight,
                        bar.Voices.Select(voice => new GuitarTabApi.Models.VoiceJson(
                            voice.Beats.Select(beat => new GuitarTabApi.Models.BeatJson(
                                (int)Math.Round(beat.DisplayStart),
                                (int)Math.Round(beat.DisplayDuration),
                                GuitarTabApi.Models.Helpers.GetProp(beat, "Duration", 0),
                                GuitarTabApi.Models.Helpers.GetProp(beat, "Dots", 0),
                                (GuitarTabApi.Models.Helpers.GetProp(beat, "TupletNumerator", 1), GuitarTabApi.Models.Helpers.GetProp(beat, "TupletDenominator", 1)),
                                beat.IsRest,
                                GuitarTabApi.Models.Helpers.GetProp(beat, "TremoloPicking", 0),
                                GuitarTabApi.Models.Helpers.GetProp(beat, "FadeIn", false),
                                GuitarTabApi.Models.Helpers.GetProp(beat, "Arpeggio", 0),
                                GuitarTabApi.Models.Helpers.GetProp(beat, "BrushDirection", 0),
                                GuitarTabApi.Models.Helpers.GetProp(beat, "WhammyBarPoints", "")?.ToString() ?? "",
                                beat.Notes.Select(n => new GuitarTabApi.Models.NoteJson(
                                    (int)n.String,
                                    (staff.Tuning?.Count ?? 6) - (int)n.String + 1,
                                    (int)n.Fret,
                                    (int)Math.Round(n.RealValue),
                                    GuitarTabApi.Models.Helpers.GetProp(n, "IsTieOrigin", false),
                                    n.IsTieDestination,
                                    n.IsGhost,
                                    n.IsDead,
                                    n.IsHarmonic,
                                    (int)n.HarmonicType,
                                    n.HarmonicValue,
                                    n.IsPalmMute,
                                    n.IsLetRing,
                                    n.IsStaccato,
                                    n.IsHammerPullOrigin,
                                    n.IsHammerPullDestination,
                                    n.IsSlurOrigin,
                                    n.IsSlurDestination,
                                    (int)n.SlideInType,
                                    (int)n.SlideOutType,
                                    (int)n.BendType,
                                    n.BendPoints?.Select(bp => new GuitarTabApi.Models.BendPointJson(bp.Offset, bp.Value)).ToArray() ?? Array.Empty<GuitarTabApi.Models.BendPointJson>(),
                                    (int)n.Vibrato,
                                    n.IsTrill,
                                    n.TrillValue,
                                    (int)n.TrillSpeed,
                                    GuitarTabApi.Models.Helpers.GetProp(n, "IsTapped", false),
                                    GuitarTabApi.Models.Helpers.GetProp(n, "IsSlapped", false),
                                    GuitarTabApi.Models.Helpers.GetProp(n, "IsPopped", false),
                                    (int)n.LeftHandFinger,
                                    (int)n.RightHandFinger
                                )).ToArray()
                            )).ToArray()
                        )).ToArray(),
                        null,
                        markerForBar
                    );
                }).ToArray()
            )).ToArray()
        )).ToArray()
    );

    var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    return Results.Text(JsonSerializer.Serialize(scoreJson, jsonOpts), "application/json");
});

app.Run();

// =============================================================
// Models & Helpers
// =============================================================
namespace GuitarTabApi.Models
{
    public record ScoreJson(string artist, string title, string album, string subtitle, string copyright, string musicBy, string wordsBy, string transcriber, string instructions, string[] notices, double tempo, int ticksPerBeat, int[] globalTuning, string[] globalTuningText, string globalTuningLetters, TimeSigJson[] timeSignatures, KeySigJson[] keySignatures, TempoChangeJson[] tempoChanges, MarkerJson[] markers, RepeatJson[] repeats, TrackJson[] tracks);
    public record TrackJson(string name, int program, int channel, bool isPercussion, int capo, int transpose, int volume, int pan, StaffJson[] staves);
    public record StaffJson(int[] tuning, string[] tuningText, string tuningLetters, BarJson[] bars);
    public record BarJson(int index, bool repeatOpen, bool repeatClose, int repeatCount, int[] voltas, int barlineType, VoiceJson[] voices, TimeSigJson? timeSigOverride, MarkerJson? marker);
    public record VoiceJson(BeatJson[] beats);
    public record BeatJson(int start, int duration, int durationSymbol, int dots, (int num, int den) tuplet, bool isRest, int tremoloPicking, bool fadeIn, int arpeggio, int brushDirection, string whammyBarPoints, NoteJson[] notes);
    public record NoteJson(int @stringLow, int @stringHigh, int fret, int pitchMidi, bool isTieOrigin, bool isTieDestination, bool isGhost, bool isDead, bool isHarmonic, int harmonicType, double harmonicValue, bool isPalmMute, bool isLetRing, bool isStaccato, bool isHammerPullOrigin, bool isHammerPullDestination, bool isSlurOrigin, bool isSlurDestination, int slideInType, int slideOutType, int bendType, BendPointJson[] bendPoints, int vibratoType, bool isTrill, double trillValue, int trillSpeed, bool isTapped, bool isSlapped, bool isPopped, int fingeringLeft, int fingeringRight);
    public record BendPointJson(double offset, double value);
    public record TimeSigJson(int numerator, int denominator, int barIndex);
    public record KeySigJson(int key, int type, int barIndex);
    public record TempoChangeJson(double bpm, int barIndex);
    public record MarkerJson(string text, int colorArgb, int barIndex);
    public record RepeatJson(bool open, bool close, int count, int barIndex);

    public static class Helpers
    {
        public static int[] AlternateEndingsToArray(double alternateEndingsBitflag)
        {
            var result = new List<int>();
            for (int i = 0; i < 8; i++)
                if (((int)alternateEndingsBitflag & (1 << i)) != 0) result.Add(i + 1);
            return result.ToArray();
        }

        public static object? GetProp(object obj, string name) =>
            obj?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);

        public static T GetProp<T>(object obj, string name, T defaultValue = default!)
        {
            var val = GetProp(obj, name);
            if (val is T t) return t;
            try { return (T)Convert.ChangeType(val, typeof(T)); } catch { return defaultValue; }
        }

        public static int[] Tunings(Staff s) =>
            s.StringTuning?.Tunings?.Select(v => (int)Math.Round(v)).ToArray() ?? Array.Empty<int>();

        public static string[] TuningText(Staff s) =>
            s.StringTuning?.Tunings?.Select(v => NoteNumberToString((int)Math.Round(v))).ToArray() ?? Array.Empty<string>();

        public static string TuningLetters(Staff s) => string.Join(" ", TuningText(s));

        public static string NoteNumberToString(int midiNote)
        {
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            return names[midiNote % 12] + (midiNote / 12);
        }

        public static TimeSigJson[] CollectTimeSigs(Score s) =>
            s.MasterBars.Select((mb, i) => new TimeSigJson((int)mb.TimeSignatureNumerator, (int)mb.TimeSignatureDenominator, i)).ToArray();

        public static KeySigJson[] CollectKeySigs(Score s) =>
            s.MasterBars.Select((mb, i) => new KeySigJson((int)mb.KeySignature, (int)mb.KeySignatureType, i)).ToArray();

        public static TempoChangeJson[] CollectTempos(Score s)
        {
            var list = new List<TempoChangeJson>();
            for (int i = 0; i < s.MasterBars.Count; i++)
            {
                var auto = GetProp(s.MasterBars[i], "TempoAutomation");
                if (auto != null)
                {
                    double bpm = GetProp(auto, "Value", 0.0);
                    if (bpm > 0) list.Add(new TempoChangeJson(bpm, i));
                }
            }
            return list.ToArray();
        }

        public static MarkerJson[] CollectMarkers(Score s)
        {
            var list = new List<MarkerJson>();
            for (int i = 0; i < s.MasterBars.Count; i++)
            {
                var section = s.MasterBars[i].Section;
                if (section != null) list.Add(new MarkerJson(section.Text ?? "", 0, i));
            }
            return list.ToArray();
        }

        public static RepeatJson[] CollectRepeats(Score s)
        {
            var list = new List<RepeatJson>();
            for (int i = 0; i < s.MasterBars.Count; i++)
            {
                var mb = s.MasterBars[i];
                if (mb.IsRepeatStart || mb.IsRepeatEnd)
                    list.Add(new RepeatJson(mb.IsRepeatStart, mb.IsRepeatEnd, (int)mb.RepeatCount, i));
            }
            return list.ToArray();
        }
    }
}
