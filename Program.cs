using System.Text.Json;
using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Model;
using Microsoft.AspNetCore.Http.Features;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);
var allowAll = "AllowAll";
builder.Services.AddCors(o => o.AddPolicy(allowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
var apiKey = builder.Configuration["API_KEY"];
var app = builder.Build();
app.UseCors(allowAll);

app.MapGet("/", () => Results.Json(new { ok = true, service = "alphaTab GP parser", formats = "GP3–GP8" }));

// Helper to extract optional slide types, bend, accentuation, etc.
static string SafeEnum(object enumObj) => enumObj?.ToString() ?? "None";

static BeatJsonExtended[] CollectBeats(Voice voice, Staff staff)
{
    return voice.Beats.Select(beat =>
    {
        var effects = beat.Effects ?? new BeatEffects();

        return new BeatJsonExtended(
            (int)Math.Round(beat.DisplayStart),
            (int)Math.Round(beat.DisplayDuration),
            beat.IsRest,
            effects.Arpeggio != null,
            effects.Tuplet?.Numerator ?? 0,
            effects.Tuplet?.Denominator ?? 0,
            effects.LetRing != null,
            effects.Any(),
            Array.Empty<int>(), // old beat.Beats removed
            effects.TremoloPicking != null,
            SafeEnum(effects.PickStroke),
            beat.Chord?.Id,
            beat.Text
        )
        {
            Notes = beat.Notes?.Select(n =>
            {
                var neffects = n.Effects ?? new NoteEffects();

                return new NoteJsonExtended(
                    (int)n.String,
                    staff.StringTuning.Tunings.Count - n.String + 1,
                    (int)n.Fret,
                    neffects.TieDestination != null,
                    neffects.TieOrigin != null,
                    neffects.Ghost != null,
                    neffects.Grace != null,
                    neffects.Dead != null,
                    neffects.Harmonic != null,
                    neffects.PalmMute != null,
                    neffects.Mute != null,
                    neffects.LetRing != null,
                    neffects.TremoloPicking != null,
                    SafeEnum(neffects.SlideInType),
                    SafeEnum(neffects.SlideOutType),
                    neffects.HammerOn != null,
                    neffects.PullOff != null,
                    neffects.Vibrato != null,
                    neffects.Trill != null,
                    neffects.Tap != null,
                    neffects.HarmonicType.ToString(),
                    neffects.Bend?.ToString(),
                    neffects.Accentuation != null,
                    SafeEnum(neffects.PickStroke),
                    (int)(neffects.LeftHandFinger ?? Fingers.Unknown),
                    (int)(neffects.RightHandFinger ?? Fingers.Unknown),
                    neffects.VelocityPercent ?? 0,
                    neffects.Any()
                );
            }).ToArray() ?? Array.Empty<NoteJsonExtended>()
        };
    }).ToArray();
}

app.MapPost("/parse", async (HttpRequest req) =>
{
    if (!string.IsNullOrEmpty(apiKey) && (!req.Headers.TryGetValue("x-api-key", out var key) || key != apiKey))
        return Results.Unauthorized();
    if (!req.HasFormContentType)
        return Results.BadRequest("multipart/form-data required");

    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file == null || file.Length == 0)
        return Results.BadRequest("Upload .gp3/.gp4/.gp5/.gpx/.gp as 'file'");

    byte[] data;
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    data = ms.ToArray();

    Score score;
    try { score = ScoreLoader.LoadScoreFromBytes(data, new Settings()); }
    catch (Exception ex) { return Results.BadRequest($"alphaTab failed to parse: {ex.Message}"); }

    if (score.Tracks == null || score.Tracks.Count == 0)
        return Results.BadRequest("No tracks found.");

    var scoreJson = new ScoreJsonExtended(
        score.Artist, score.Title, score.Album, score.SubTitle,
        score.Copyright, score.Music, score.Words, score.Tab,
        score.Instructions, score.Notices?.Split('\n') ?? Array.Empty<string>(),
        score.Tempo, 480,
        score.MasterBars.Select(mb =>
            new TimeSigJson(mb.TimeSignature.Numerator, mb.TimeSignature.Denominator, score.MasterBars.IndexOf(mb))
        ).ToArray(),
        score.MasterBars.Select(mb =>
            new KeySigJson((int)mb.KeySignature, (int)mb.KeySignatureType, score.MasterBars.IndexOf(mb))
        ).ToArray(),
        score.MasterBars.Select(mb =>
            new TempoChangeJson(mb.TempoAutomation?.Value ?? score.Tempo, score.MasterBars.IndexOf(mb))
        ).ToArray(),
        score.Tracks.Select(track =>
            new TrackJsonExtended(
                track.Name,
                track.Staves.Select(staff =>
                    new StaffJsonExtended(
                        staff.StringTuning?.Tunings.Select(v => (int)Math.Round(v)).ToArray() ?? Array.Empty<int>(),
                        staff.Bars.Select(bar =>
                            new BarJsonExtended(
                                bar.Index,
                                bar.Voices.Select(v => new VoiceJsonExtended(CollectBeats(v, staff))).ToArray(),
                                bar.MasterBar.RepeatClose, // adjusted for new API
                                bar.MasterBar.IsRepeatOpen,
                                bar.MasterBar.IsRepeatClose,
                                bar.MasterBar.AlternateEndings,
                                bar.MasterBar.Marker?.Title,
                                bar.MasterBar.IsSimile
                            )
                        ).ToArray()
                    )
                ).ToArray()
            )
        ).ToArray()
    );

    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    return Results.Text(JsonSerializer.Serialize(scoreJson, options), "application/json");
});

app.Run();

// --- Extended JSON models (record types) ---

record ScoreJsonExtended(
    string artist, string title, string album, string subtitle,
    string copyright, string musicBy, string wordsBy, string transcriber,
    string instructions, string[] notices, double tempo, int ticksPerBeat,
    TimeSigJson[] timeSignatures, KeySigJson[] keySignatures, TempoChangeJson[] tempoChanges,
    TrackJsonExtended[] tracks
);

record TrackJsonExtended(string name, StaffJsonExtended[] staves);

record StaffJsonExtended(int[] tuning, BarJsonExtended[] bars);

record BarJsonExtended(int index, VoiceJsonExtended[] voices,
    int repeatCount, bool startRepeat, bool endRepeat,
    int alternateEndingNumber, string barMarker, bool isSimileMark
);

record VoiceJsonExtended(BeatJsonExtended[] beats);

record BeatJsonExtended(int start, int duration, bool isRest,
    bool isArpeggio, int tupletNumerator, int tupletDenominator,
    bool letRing, bool beatEffects, int[] beatStarts,
    bool isTremoloPicking, string pickStroke, string chordId, string beatText)
{
    public NoteJsonExtended[] Notes { get; set; }
}

record NoteJsonExtended(
    int stringLow, int stringHigh, int fret, bool isTieDestination, bool isTieOrigin,
    bool isGhost, bool isGrace, bool isDead, bool isHarmonic,
    bool isPalmMute, bool isMuted, bool isLetRing, bool isTremoloPicking,
    string slideInType, string slideOutType, bool isHammerOn, bool isPullOff,
    bool vibrato, bool trill, bool isTapped, string harmonicType,
    string bend, bool accentuated, string pickStroke,
    int leftHandFinger, int rightHandFinger, int velocity, bool noteEffects
);

record TimeSigJson(int numerator, int denominator, int barIndex);
record KeySigJson(int key, int type, int barIndex);
record TempoChangeJson(double bpm, int barIndex);
