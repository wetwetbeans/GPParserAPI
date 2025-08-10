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

// Helper to safely stringify enums or nullables
static string SafeEnum(object? enumObj) => enumObj?.ToString() ?? "None";

static BeatJsonExtended[] CollectBeats(Voice voice, Staff staff)
{
    return voice.Beats.Select(beat =>
    {
        // AlphaTab 1.6.x: Effects are now properties on Beat and Note directly
        return new BeatJsonExtended(
            (int)Math.Round(beat.Start),          // DisplayStart removed, use Start
            (int)Math.Round(beat.Duration),       // DisplayDuration removed, use Duration
            beat.IsRest,
            beat.Arpeggio != null,
            beat.TupletNumerator,
            beat.TupletDenominator,
            beat.LetRing,
            beat.HasAnyEffect, // direct check
            Array.Empty<int>(),
            beat.TremoloPicking != null,
            SafeEnum(beat.PickStroke),
            beat.Chord?.Name, // Id removed, using Name or null
            beat.Text
        )
        {
            Notes = beat.Notes?.Select(n =>
            {
                return new NoteJsonExtended(
                    (int)n.String,
                    staff.StringTuning.Tunings.Count - n.String + 1,
                    (int)n.Fret,
                    n.TieDestination != null,
                    n.TieOrigin != null,
                    n.IsGhost,
                    n.IsGrace,
                    n.IsDead,
                    n.IsHarmonic,
                    n.PalmMute,
                    n.Mute,
                    n.LetRing,
                    n.TremoloPicking != null,
                    SafeEnum(n.SlideInType),
                    SafeEnum(n.SlideOutType),
                    n.HammerOn,
                    n.PullOff,
                    n.Vibrato,
                    n.Trill != null,
                    n.Tap,
                    SafeEnum(n.HarmonicType),
                    n.Bend != null ? n.Bend.ToString() : "",
                    n.Accentuated,
                    SafeEnum(n.PickStroke),
                    (int)(n.LeftHandFinger ?? Fingers.Unknown),
                    (int)(n.RightHandFinger ?? Fingers.Unknown),
                    n.VelocityPercent ?? 0,
                    n.HasAnyEffect
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
        score.MasterBars.Select((mb, idx) =>
            new TimeSigJson(mb.TimeSignatureNumerator, mb.TimeSignatureDenominator, idx)
        ).ToArray(),
        score.MasterBars.Select((mb, idx) =>
            new KeySigJson((int)mb.KeySignature, (int)mb.KeySignatureType, idx)
        ).ToArray(),
        score.MasterBars.Select((mb, idx) =>
            new TempoChangeJson(mb.Tempo, idx)
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
                                bar.MasterBar.RepeatCount,
                                bar.MasterBar.IsRepeatOpen,
                                bar.MasterBar.IsRepeatClose,
                                bar.MasterBar.AlternateEndings,
                                bar.MasterBar.Marker?.Title ?? "",
                                bar.MasterBar.IsSimileMark
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
