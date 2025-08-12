using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Model;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Upload size
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

// CORS
var allowAll = "AllowAll";
builder.Services.AddCors(o => o.AddPolicy(allowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var apiKey = builder.Configuration["API_KEY"];

var app = builder.Build();
app.UseCors(allowAll);

// Health check
app.MapGet("/", () => Results.Json(new { ok = true, service = "alphaTab GP parser", formats = "GP3–GP8" }));

// ------------ helpers ------------
static int[] Tunings(Staff s)
{
    var list = s.StringTuning?.Tunings;
    if (list == null || list.Count == 0) return Array.Empty<int>();
    return list.Select(v => (int)Math.Round(v)).ToArray();
}

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

static KeySigJson[] CollectKeySigs(Score s)
{
    var list = new List<KeySigJson>();
    try
    {
        for (int i = 0; i < s.MasterBars.Count; i++)
        {
            var mb = s.MasterBars[i];
            var ksProp = mb.GetType().GetProperty("KeySignature");
            var kstProp = mb.GetType().GetProperty("KeySignatureType");
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

static bool GetIsGrace(Note n)
{
    var prop = typeof(Note).GetProperty("GraceType");
    if (prop != null)
    {
        var val = prop.GetValue(n);
        if (val != null && val.ToString() != "None") return true;
    }
    return false;
}

static int GetVelocity(Note n)
{
    var prop = typeof(Note).GetProperty("VelocityPercent") ?? typeof(Note).GetProperty("Velocity");
    if (prop != null)
    {
        var val = prop.GetValue(n);
        if (val != null && int.TryParse(val.ToString(), out int v))
            return v;
    }
    return 0;
}

// Auto-grouping timing fix
static (bool origin, bool destination) GetFixedLegatoFlags(Note note, int beatIndex, List<Beat> beats, int maxTickGap = 120)
{
    bool origin = note.IsHammerPullOrigin;
    bool destination = note.IsHammerPullDestination;

    int currentStart = beats[beatIndex].DisplayStart;

    // Check previous note
    for (int pb = beatIndex - 1; pb >= 0; pb--)
    {
        var prevBeat = beats[pb];
        var prevNote = prevBeat?.Notes?.FirstOrDefault(n => n.String == note.String);
        if (prevNote != null)
        {
            int gap = currentStart - prevBeat.DisplayStart;
            if (gap <= maxTickGap && (prevNote.IsHammerPullOrigin || prevNote.IsHammerPullDestination))
            {
                destination = true;
            }
            break;
        }
    }

    // Check next note
    for (int nb = beatIndex + 1; nb < beats.Count; nb++)
    {
        var nextBeat = beats[nb];
        var nextNote = nextBeat?.Notes?.FirstOrDefault(n => n.String == note.String);
        if (nextNote != null)
        {
            int gap = nextBeat.DisplayStart - currentStart;
            if (gap <= maxTickGap && (nextNote.IsHammerPullDestination || nextNote.IsHammerPullOrigin))
            {
                origin = true;
            }
            break;
        }
    }

    return (origin, destination);
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

    var scoreJson = new ScoreJson(
        score.Artist ?? "(Unknown Artist)",
        score.Title ?? "(Untitled)",
        score.Album ?? "",
        score.SubTitle ?? "",
        score.Copyright ?? "",
        score.Music ?? "",
        score.Words ?? "",
        score.Tab ?? "",
        score.Instructions ?? "",
        string.IsNullOrWhiteSpace(score.Notices) ? Array.Empty<string>() : new[] { score.Notices },
        score.Tempo > 0 ? score.Tempo : 120.0,
        480,
        Array.Empty<int>(),
        Array.Empty<string>(),
        "",
        CollectTimeSigs(score),
        CollectKeySigs(score),
        CollectTempos(score),
        score.Tracks.Select(track =>
            new TrackJson(
                track.Name ?? "",
                track.Staves.Select(staff =>
                    new StaffJson(
                        Tunings(staff),
                        (staff.Bars ?? new List<Bar>()).Select((bar, barIdx) =>
                            new BarJson(
                                barIdx,
                                (bar.Voices ?? new List<Voice>()).Select(voice =>
                                    new VoiceJson(
                                        (voice.Beats ?? new List<Beat>()).Select((beat, beatIndex) =>
                                            new BeatJson(
                                                (int)Math.Round(beat.DisplayStart),
                                                (int)Math.Round(beat.DisplayDuration),
                                                (beat.Notes ?? new List<Note>()).Select(n =>
                                                {
                                                    int stringCount = staff.StringTuning?.Tunings?.Count ?? 6;
                                                    int low = (int)n.String;
                                                    int high = stringCount > 0 ? (stringCount - low + 1) : low;

                                                    var (fixedOrigin, fixedDestination) = GetFixedLegatoFlags(n, beatIndex, voice.Beats);

                                                    return new NoteJson(
                                                        low,
                                                        high,
                                                        (int)n.Fret,
                                                        n.IsTieDestination,
                                                        n.IsGhost,
                                                        GetIsGrace(n),
                                                        n.IsDead,
                                                        n.IsHarmonic,
                                                        n.IsPalmMute,
                                                        GetVelocity(n),
                                                        n.IsLetRing,
                                                        n.IsStaccato,
                                                        fixedOrigin,
                                                        fixedDestination,
                                                        n.IsSlurOrigin,
                                                        n.IsSlurDestination,
                                                        (int)n.SlideInType,
                                                        (int)n.SlideOutType,
                                                        (int)n.BendType,
                                                        n.BendPoints?.Select(bp => new BendPointJson(bp.Offset, bp.Value)).ToArray() ?? Array.Empty<BendPointJson>(),
                                                        (int)n.Vibrato,
                                                        n.HarmonicValue,
                                                        n.IsTrill,
                                                        n.TrillValue,
                                                        (int)n.TrillSpeed
                                                    );
                                                }).ToArray(),
                                                beat.IsRest
                                            )
                                        ).ToArray()
                                    )
                                ).ToArray(),
                                null
                            )
                        ).ToArray()
                    )
                ).ToArray()
            )
        ).ToArray()
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
    string[] globalTuningText,
    string globalTuningLetters,
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

record NoteJson(
    int @stringLow,
    int @stringHigh,
    int fret,
    bool isTieDestination,
    bool isGhost,
    bool isGrace,
    bool isDead,
    bool isHarmonic,
    bool isPalmMute,
    int velocity,
    bool isLetRing,
    bool isStaccato,
    bool isHammerPullOrigin,
    bool isHammerPullDestination,
    bool isSlurOrigin,
    bool isSlurDestination,
    int slideInType,
    int slideOutType,
    int bendType,
    BendPointJson[] bendPoints,
    int vibratoType,
    double harmonicValue,
    bool isTrill,
    double trillValue,
    int trillSpeed
);

record BendPointJson(double offset, double value);
record TimeSigJson(int numerator, int denominator, int barIndex);
record KeySigJson(int key, int type, int barIndex);
record TempoChangeJson(double bpm, int barIndex);
