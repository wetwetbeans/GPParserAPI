using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Model;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using System.Linq;
using System.Reflection;

// ---------------- Top-level Program ----------------
var builder = WebApplication.CreateBuilder(args);

// Allow big file uploads
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

// CORS
var allowAll = "AllowAll";
builder.Services.AddCors(o => o.AddPolicy(allowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var apiKey = builder.Configuration["API_KEY"];

var app = builder.Build();
app.UseCors(allowAll);

// Health check
app.MapGet("/", () => Results.Json(new { ok = true, service = "AlphaTab GP parser", formats = "GP3–GP8" }));

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

    int tpb = GetProp(score, "TicksPerBeat", 480);

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
        tpb,
        Array.Empty<int>(),
        Array.Empty<string>(),
        "",
        CollectTimeSigs(score),
        CollectKeySigs(score),
        CollectTempos(score),
        CollectMarkers(score),
        CollectRepeats(score),
        score.Tracks.Select(track =>
            new TrackJson(
                track.Name ?? "",
                GetProp(track, "Program", 0),
                GetProp(track, "Channel", 0),
                GetProp(track, "IsPercussion", false),
                GetProp(track, "Capo", 0),
                GetProp(track, "Transpose", 0),
                GetProp(track, "Volume", 100),
                GetProp(track, "Pan", 0),
                track.Staves.Select(staff =>
                    new StaffJson(
                        Tunings(staff),
                        TuningText(staff),
                        TuningLetters(staff),
                        staff.Bars.Select((bar, barIdx) =>
                        {
                            var markerForBar = bar.Section != null
                                ? new MarkerJson(bar.Section.Text ?? "", bar.Section.Color.ToArgb(), barIdx)
                                : null;

                            return new BarJson(
                                barIdx,
                                GetProp(bar, "RepeatOpen", false),
                                GetProp(bar, "RepeatClose", false),
                                GetProp(bar, "RepeatCount", 0),
                                GetProp(bar, "VoltaAlternatives", Array.Empty<int>()),
                                GetProp(bar, "BarlineType", 0),
                                bar.Voices.Select(voice =>
                                    new VoiceJson(
                                        voice.Beats.Select(beat =>
                                            new BeatJson(
                                                (int)Math.Round(beat.DisplayStart),
                                                (int)Math.Round(beat.DisplayDuration),
                                                GetProp(beat, "Duration", 0),
                                                GetProp(beat, "Dots", 0),
                                                (GetProp(beat, "TupletNumerator", 1), GetProp(beat, "TupletDenominator", 1)),
                                                beat.IsRest,
                                                GetProp(beat, "TremoloPicking", 0),
                                                GetProp(beat, "FadeIn", false),
                                                GetProp(beat, "Arpeggio", 0),
                                                GetProp(beat, "BrushDirection", 0),
                                                GetProp(beat, "WhammyBarPoints", Array.Empty<object>()).ToString(),
                                                beat.Notes.Select(n =>
                                                    new NoteJson(
                                                        (int)n.String,
                                                        (staff.StringTuning?.Tunings?.Count ?? 6) - (int)n.String + 1,
                                                        (int)n.Fret,
                                                        n.Pitch,
                                                        GetProp(n, "IsTieOrigin", false),
                                                        n.IsTieDestination,
                                                        n.IsGhost,
                                                        n.IsDead,
                                                        n.IsHarmonic,
                                                        GetProp(n, "HarmonicType", 0),
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
                                                        n.BendPoints?.Select(bp => new BendPointJson(bp.Offset, bp.Value)).ToArray() ?? Array.Empty<BendPointJson>(),
                                                        (int)n.Vibrato,
                                                        n.IsTrill,
                                                        n.TrillValue,
                                                        (int)n.TrillSpeed,
                                                        GetProp(n, "IsTapped", false),
                                                        GetProp(n, "IsSlapped", false),
                                                        GetProp(n, "IsPopped", false),
                                                        GetProp(n, "FingeringLeft", 0),
                                                        GetProp(n, "FingeringRight", 0)
                                                    )
                                                ).ToArray()
                                            )
                                        ).ToArray()
                                    )
                                ).ToArray(),
                                null,
                                markerForBar
                            );
                        }).ToArray()
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

// ---------------- JSON Records ----------------
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
    MarkerJson[] markers,
    RepeatJson[] repeats,
    TrackJson[] tracks
);

record TrackJson(string name, int program, int channel, bool isPercussion, int capo, int transpose, int volume, int pan, StaffJson[] staves);
record StaffJson(int[] tuning, string[] tuningText, string tuningLetters, BarJson[] bars);
record BarJson(int index, bool repeatOpen, bool repeatClose, int repeatCount, int[] voltas, int barlineType, VoiceJson[] voices, TimeSigJson? timeSigOverride, MarkerJson? marker);
record VoiceJson(BeatJson[] beats);
record BeatJson(int start, int duration, int durationSymbol, int dots, (int num, int den) tuplet, bool isRest, int tremoloPicking, bool fadeIn, int arpeggio, int brushDirection, string whammyBarPoints, NoteJson[] notes);
record NoteJson(int @stringLow, int @stringHigh, int fret, int pitchMidi, bool isTieOrigin, bool isTieDestination, bool isGhost, bool isDead, bool isHarmonic, int harmonicType, double harmonicValue, bool isPalmMute, bool isLetRing, bool isStaccato, bool isHammerPullOrigin, bool isHammerPullDestination, bool isSlurOrigin, bool isSlurDestination, int slideInType, int slideOutType, int bendType, BendPointJson[] bendPoints, int vibratoType, bool isTrill, double trillValue, int trillSpeed, bool isTapped, bool isSlapped, bool isPopped, int fingeringLeft, int fingeringRight);
record BendPointJson(double offset, double value);
record TimeSigJson(int numerator, int denominator, int barIndex);
record KeySigJson(int key, int type, int barIndex);
record TempoChangeJson(double bpm, int barIndex);
record MarkerJson(string text, int colorArgb, int barIndex);
record RepeatJson(bool open, bool close, int count, int barIndex);

// ---------------- Helper Methods ----------------
static object? GetProp(object obj, string name) =>
    obj?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);

static T GetProp<T>(object obj, string name, T defaultValue = default!)
{
    var val = GetProp(obj, name);
    if (val is T t) return t;
    try { return (T)Convert.ChangeType(val, typeof(T)); }
    catch { return defaultValue; }
}

static int[] Tunings(Staff s) =>
    s.StringTuning?.Tunings?.Select(v => (int)Math.Round(v)).ToArray() ?? Array.Empty<int>();

static string[] TuningText(Staff s) =>
    s.StringTuning?.Tunings?.Select(v => NoteNumberToString((int)Math.Round(v))).ToArray() ?? Array.Empty<string>();

static string TuningLetters(Staff s) =>
    string.Join(" ", TuningText(s));

static string NoteNumberToString(int midiNote)
{
    string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    return names[midiNote % 12] + (midiNote / 12);
}

static TimeSigJson[] CollectTimeSigs(Score s) =>
    s.MasterBars.Select((mb, i) => new TimeSigJson(mb.TimeSignatureNumerator, mb.TimeSignatureDenominator, i)).ToArray();

static KeySigJson[] CollectKeySigs(Score s) =>
    s.MasterBars.Select((mb, i) => new KeySigJson((int)mb.KeySignature, (int)mb.KeySignatureType, i)).ToArray();

static TempoChangeJson[] CollectTempos(Score s)
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

static MarkerJson[] CollectMarkers(Score s)
{
    var list = new List<MarkerJson>();
    for (int i = 0; i < s.MasterBars.Count; i++)
    {
        var section = s.MasterBars[i].Section;
        if (section != null)
            list.Add(new MarkerJson(section.Text ?? "", section.Color.ToArgb(), i));
    }
    return list.ToArray();
}

static RepeatJson[] CollectRepeats(Score s)
{
    var list = new List<RepeatJson>();
    for (int i = 0; i < s.MasterBars.Count; i++)
    {
        var mb = s.MasterBars[i];
        if (mb.RepeatClose || mb.RepeatOpen)
            list.Add(new RepeatJson(mb.RepeatOpen, mb.RepeatClose, mb.RepeatCount, i));
    }
    return list.ToArray();
}
