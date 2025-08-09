using System.Text.Json;
using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Model;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Upload size
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

// CORS (WebGL, mobile)
var allowAll = "AllowAll";
builder.Services.AddCors(o => o.AddPolicy(allowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// Optional API key (set API_KEY in Render)
var apiKey = builder.Configuration["API_KEY"];

var app = builder.Build();
app.UseCors(allowAll);

// Health check endpoint
app.MapGet("/", () => Results.Json(new
{
    ok = true,
    service = "alphaTab GP parser",
    formats = "GP3–GP8"
}));

// ------------ helpers ------------
static int[] Tunings(Staff s)
{
    var list = s.StringTuning?.Tunings;
    if (list == null || list.Count == 0) return Array.Empty<int>();
    return list.Select(v => (int)Math.Round(v)).ToArray();
}

// alphaTab 1.6.x: read numerator/denominator from MasterBar via props
static TimeSigJson[] CollectTimeSigs(Score s)
{
    var list = new List<TimeSigJson>();
    try
    {
        for (int i = 0; i < s.MasterBars.Count; i++)
        {
            var mb = s.MasterBars[i];
            var numProp = mb.GetType().GetProperty("TimeSignatureNumerator");
            var denProp = mb.GetType().GetProperty("TimeSignatureDenominator");
            if (numProp != null && denProp != null)
            {
                int num = Convert.ToInt32(numProp.GetValue(mb) ?? 0);
                int den = Convert.ToInt32(denProp.GetValue(mb) ?? 0);
                if (num > 0 && den > 0) list.Add(new TimeSigJson(num, den, i));
            }
        }
    }
    catch { }
    return list.ToArray();
}

// Collect key signatures per master bar (reflection for version safety)
static KeySigJson[] CollectKeySigs(Score s)
{
    var list = new List<KeySigJson>();
    try
    {
        for (int i = 0; i < s.MasterBars.Count; i++)
        {
            var mb = s.MasterBars[i];
            var ksProp = mb.GetType().GetProperty("KeySignature");       // int flats/sharps
            var kstProp = mb.GetType().GetProperty("KeySignatureType");   // 0=major, 1=minor (in alphaTab)
            if (ksProp != null)
            {
                int ks = Convert.ToInt32(ksProp.GetValue(mb) ?? 0);
                int kst = kstProp != null ? Convert.ToInt32(kstProp.GetValue(mb) ?? 0) : 0;
                list.Add(new KeySigJson(ks, kst, i));
            }
        }
    }
    catch { }
    return list.ToArray();
}

// Collect tempo changes per master bar (reads automation if present)
static TempoChangeJson[] CollectTempos(Score s)
{
    var list = new List<TempoChangeJson>();
    try
    {
        for (int i = 0; i < s.MasterBars.Count; i++)
        {
            var mb = s.MasterBars[i];
            var tempoAutoProp = mb.GetType().GetProperty("TempoAutomation");
            if (tempoAutoProp != null)
            {
                var auto = tempoAutoProp.GetValue(mb);
                if (auto != null)
                {
                    var valueProp = auto.GetType().GetProperty("Value");
                    if (valueProp != null)
                    {
                        double bpm = Convert.ToDouble(valueProp.GetValue(auto) ?? 0);
                        if (bpm > 0) list.Add(new TempoChangeJson(bpm, i));
                    }
                }
            }
        }
    }
    catch { }
    return list.ToArray();
}

// ------------ /parse endpoint ------------
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
        score = ScoreLoader.LoadScoreFromBytes(data, new Settings());
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"alphaTab failed to parse: {ex.Message}");
    }

    if (score.Tracks == null || score.Tracks.Count == 0)
        return Results.BadRequest("No tracks found.");

    Track PickTrack()
    {
        if (trackIndex is int idx && idx >= 0 && idx < score.Tracks.Count)
            return score.Tracks[idx];

        return score.Tracks.FirstOrDefault(t =>
            t.Staves != null &&
            t.Staves.Count > 0 &&
            !t.Staves[0].IsPercussion &&
            t.Staves[0].StringTuning?.Tunings is { Count: >= 4 and <= 7 }
        ) ?? score.Tracks[0];
    }

    var tr = PickTrack();
    var staff = (tr.Staves != null && tr.Staves.Count > 0) ? tr.Staves[0] : null;
    if (staff == null)
        return Results.BadRequest("Selected track has no staves.");

    // Top-level metadata (strict placeholders for title/artist)
    var title = !string.IsNullOrWhiteSpace(score.Title) ? score.Title : "(Untitled)";
    var artist = !string.IsNullOrWhiteSpace(score.Artist) ? score.Artist : "(Unknown Artist)";
    var album = score.Album ?? "";
    var subtitle = score.SubTitle ?? "";
    var copyright = score.Copyright ?? "";
    var musicBy = score.Music ?? "";      // composer
    var wordsBy = score.Words ?? "";      // lyricist
    var transcriber = score.Tab ?? "";        // tab author
    var instructions = score.Instructions ?? "";
    var notices = (score.Notices != null && score.Notices.Count > 0) ? score.Notices.ToArray() : Array.Empty<string>();

    // Global/top-of-file musical context
    var baseTempoBpm = score.Tempo > 0 ? score.Tempo : 120.0;
    var ticksPerBeat = 480; // convention you’re using for DisplayStart/Duration
    var globalTuning = Tunings(staff);
    var timeSigs = CollectTimeSigs(score);
    var keySigs = CollectKeySigs(score);
    var tempoChanges = CollectTempos(score);

    var scoreJson = new ScoreJson(
        title: title,
        artist: artist,
        album: album,
        subtitle: subtitle,
        copyright: copyright,
        musicBy: musicBy,
        wordsBy: wordsBy,
        transcriber: transcriber,
        instructions: instructions,
        notices: notices,
        tempo: baseTempoBpm,
        ticksPerBeat: ticksPerBeat,
        globalTuning: globalTuning,
        timeSignatures: timeSigs,
        keySignatures: keySigs,
        tempoChanges: tempoChanges,
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
                                                    int low = (int)n.String; // 1..N, 1 = lowest (alphaTab)
                                                    int high = stringCount > 0 ? (stringCount - low + 1) : low; // 1 = highest for tab UI
                                                    return new NoteJson(low, high, (int)n.Fret);
                                                }).ToArray(),
                                                isRest: beat.IsRest
                                            )
                                        ).ToArray()
                                    )
                                ).ToArray(),
                                timeSigOverride: null
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
        WriteIndented = true
    };

    return Results.Text(JsonSerializer.Serialize(scoreJson, jsonOpts), "application/json");
});

app.Run();

// ------------ JSON models ------------
record ScoreJson(
    string artist,
    string title,
    string album,
    string subtitle,
    string copyright,
    string musicBy,
    string wordsBy,
    string transcriber,
    string instructions,
    string[] notices,
    double tempo,
    int ticksPerBeat,
    int[] globalTuning,
    TimeSigJson[] timeSignatures,
    KeySigJson[] keySignatures,
    TempoChangeJson[] tempoChanges,
    TrackJson[] tracks
);

record TrackJson(string name, StaffJson[] staves);
record StaffJson(int[] tuning, BarJson[] bars);
record BarJson(int index, VoiceJson[] voices, TimeSigJson? timeSigOverride);
record VoiceJson(BeatJson[] beats);
record BeatJson(int start, int duration, NoteJson[] notes, bool isRest);
record NoteJson(int @stringLow, int @stringHigh, int fret);
record TimeSigJson(int numerator, int denominator, int barIndex);
record KeySigJson(int keySignature, int keyType, int barIndex);
record TempoChangeJson(double bpm, int barIndex);
