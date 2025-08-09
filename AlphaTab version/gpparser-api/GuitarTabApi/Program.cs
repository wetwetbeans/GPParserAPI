using System.Text.Json;
using System.Text.Json.Serialization;
using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Model;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Allow GP uploads up to ~20MB
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

// CORS so Unity (incl. WebGL) can call this API
var allowAll = "AllowAll";
builder.Services.AddCors(o =>
    o.AddPolicy(allowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// Optional API key (set API_KEY in Render env)
var apiKey = builder.Configuration["API_KEY"];

var app = builder.Build();
app.UseCors(allowAll);

// Health
app.MapGet("/", () => Results.Json(new { ok = true, service = "alphaTab GP parser", formats = "GP3–GP8" }));

// ---- helpers ----
static int[] Tunings(Staff s)
{
    var list = s.StringTuning?.Tunings;
    if (list == null || list.Count == 0) return Array.Empty<int>();
    return list.Select(v => (int)Math.Round(v)).ToArray();
}

// Time signatures per master bar (alphaTab 1.6.x: use Numerator/Denominator properties on MasterBar)
static TimeSigJson[] CollectTimeSigs(Score s)
{
    var list = new List<TimeSigJson>();
    try
    {
        for (int i = 0; i < s.MasterBars.Count; i++)
        {
            var mb = s.MasterBars[i];

            // Try direct properties (preferred in 1.6.x)
            var numProp = mb.GetType().GetProperty("TimeSignatureNumerator");
            var denProp = mb.GetType().GetProperty("TimeSignatureDenominator");

            int num = 0, den = 0;
            if (numProp != null && denProp != null)
            {
                num = Convert.ToInt32(numProp.GetValue(mb) ?? 0);
                den = Convert.ToInt32(denProp.GetValue(mb) ?? 0);
            }

            if (num > 0 && den > 0)
                list.Add(new TimeSigJson(num, den, i));
        }
    }
    catch { /* swallow */ }
    return list.ToArray();
}

// ---- /parse ----
app.MapPost("/parse", async (HttpRequest req) =>
{
    if (!string.IsNullOrEmpty(apiKey))
    {
        if (!req.Headers.TryGetValue("x-api-key", out var key) || key != apiKey)
            return Results.Unauthorized();
    }

    if (!req.HasFormContentType) return Results.BadRequest("multipart/form-data required");
    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file == null || file.Length == 0) return Results.BadRequest("Upload .gp3/.gp4/.gp5/.gpx/.gp as 'file'");

    int? trackIndex = null;
    if (form.TryGetValue("trackIndex", out var tiVal) && int.TryParse(tiVal, out var tiParsed))
        trackIndex = tiParsed;

    byte[] data;
    using (var ms = new MemoryStream())
    {
        await file.CopyToAsync(ms);
        data = ms.ToArray();
    }

    Score score;
    try
    {
        var settings = new Settings();
        score = ScoreLoader.LoadScoreFromBytes(data, settings);
    }
    catch (Exception ex) { return Results.BadRequest($"alphaTab failed to parse: {ex.Message}"); }

    if (score.Tracks == null || score.Tracks.Count == 0)
        return Results.BadRequest("No tracks found.");

    Track PickTrack()
    {
        if (trackIndex.HasValue && trackIndex.Value >= 0 && trackIndex.Value < score.Tracks.Count)
            return score.Tracks[trackIndex.Value];

        return score.Tracks.FirstOrDefault(t =>
            t.Staves != null &&
            t.Staves.Count > 0 &&
            !t.Staves[0].IsPercussion &&
            t.Staves[0].StringTuning?.Tunings != null &&
            t.Staves[0].StringTuning.Tunings.Count >= 4 &&
            t.Staves[0].StringTuning.Tunings.Count <= 7
        ) ?? score.Tracks[0];
    }

    var tr = PickTrack();
    var staves = tr.Staves ?? new List<Staff>();
    var staff = staves.Count > 0 ? staves[0] : null;
    if (staff == null) return Results.BadRequest("Selected track has no staves.");

    var timeSigs = CollectTimeSigs(score);

    // Minimal, stable JSON: ticks + notes (string/fret), plus tuning and time signatures
    var scoreJson = new ScoreJson(
        title: score.Title ?? "",
        artist: score.Artist ?? "",
        tempo: score.Tempo > 0 ? score.Tempo : 120.0,
        ticksPerBeat: 480,
        timeSignatures: timeSigs,
        tracks: new[] {
            new TrackJson(
                name: tr.Name ?? "",
                staves: new[] {
                    new StaffJson(
                        tuning: Tunings(staff),
                        bars: (staff.Bars ?? new List<Bar>()).Select((bar, barIdx) =>
                            new BarJson(
                                index: barIdx,
                                voices: (bar.Voices ?? new List<Voice>()).Select(voice =>
                                    new VoiceJson(
                                        beats: (voice.Beats ?? new List<Beat>()).Select(beat =>
                                            new BeatJson(
                                                start: (int)Math.Round(beat.DisplayStart),
                                                duration: (int)Math.Round(beat.DisplayDuration),
                                                notes: (beat.Notes ?? new List<Note>()).Select(n =>
                                                {
                                                    int stringCount = staff.StringTuning?.Tunings?.Count ?? 6;
                                                    int low = (int)n.String; // 1..N, 1 = lowest (alphaTab native)
                                                    int high = stringCount > 0 ? (stringCount - low + 1) : low; // 1 = highest for tab UI

                                                    return new NoteJson(
                                                        @stringLow: low,
                                                        @stringHigh: high,
                                                        fret: (int)n.Fret
                                                    );
                                                }).ToArray(),
                                                isRest: beat.IsRest
                                            )
                                        ).ToArray()
                                    )
                                ).ToArray(),
                                timeSigOverride: null // simple: rely on master bars above
                            )
                        ).ToArray()
                    )
                }
            )
        }
    );

    var jsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    return Results.Text(JsonSerializer.Serialize(scoreJson, jsonOpts), "application/json");
});

app.Run();

// ---- JSON models ----
record ScoreJson(
    string title,
    string artist,
    double tempo,
    int ticksPerBeat,
    TimeSigJson[] timeSignatures,
    TrackJson[] tracks
);

record TrackJson(string name, StaffJson[] staves);
record StaffJson(int[] tuning, BarJson[] bars);
record BarJson(int index, VoiceJson[] voices, TimeSigJson? timeSigOverride);
record VoiceJson(BeatJson[] beats);
record BeatJson(int start, int duration, NoteJson[] notes, bool isRest);
record NoteJson(int @stringLow, int @stringHigh, int fret);
record TimeSigJson(int numerator, int denominator, int barIndex);
