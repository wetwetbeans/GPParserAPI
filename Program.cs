using System.IO;
using System.Text.Json;
using System.Linq;
using AlphaTab.Core.EcmaScript;
using AlphaTab.Importer;
using AlphaTab.Model;
using GPParser.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// simple API key
const string API_KEY = "13";
app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Headers.TryGetValue("x-api-key", out var key) || key != API_KEY)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("Unauthorized");
        return;
    }
    await next();
});

// Parse endpoint
app.MapPost("/parse", async (HttpRequest request) =>
{
    if (!request.HasFormContentType) return Results.BadRequest("Expected multipart/form-data");

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];
    if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded");

    Score score;
    using (var ms = new MemoryStream())
    {
        await file.CopyToAsync(ms);
        var u8 = new Uint8Array(ms.ToArray());
        score = ScoreLoader.LoadScoreFromBytes(u8);
    }

    var export = new ExportScore
    {
        Title = score.Title ?? "",
        Artist = score.Artist ?? "",
        Album = score.Album ?? "",
        Copyright = score.Copyright ?? "",
        Words = score.Words ?? "",
        Tempo = score.Tempo,
        TicksPerBeat = 960,

        // ✅ Score-level time signatures
        TimeSignatures = score.MasterBars
            .Select((mb, i) => new ExportTimeSignature(i, mb.TimeSignatureNumerator, mb.TimeSignatureDenominator))
            .ToList(),

        MasterBars = score.MasterBars.Select((mb, i) => new ExportMasterBar
        {
            Index = i,
            Start = mb.Start,
            Duration = mb.CalculateDuration(),
            TimeSignatureNumerator = mb.TimeSignatureNumerator,
            TimeSignatureDenominator = mb.TimeSignatureDenominator,
            IsFreeTime = mb.IsFreeTime,
            IsDoubleBar = mb.IsDoubleBar,
            IsRepeatStart = mb.IsRepeatStart,
            IsRepeatEnd = mb.IsRepeatEnd,
            RepeatCount = mb.RepeatCount,
            IsAnacrusis = mb.IsAnacrusis,
            TempoBpm = mb.TempoAutomation?.Value,
            SectionName = mb.Section?.Text
        }).ToList(),

        Tracks = score.Tracks.Select((t, i) => new ExportTrack
        {
            Index = i,
            Name = t.Name ?? "",
            ShortName = t.ShortName ?? "",
            PlaybackInfo = new ExportPlaybackInfo
            {
                Program = t.PlaybackInfo?.Program ?? 0,
                Volume = t.PlaybackInfo?.Volume ?? 100
            },
            Staves = t.Staves.Select((s, si) => new ExportStaff
            {
                Index = si,
                Tuning = s.Tuning?.Select(x => (int)x).ToList() ?? new List<int>(), // ✅ FIXED
                TuningName = s.TuningName ?? "",
                Capo = (int)s.Capo,
                TranspositionPitch = (int)s.TranspositionPitch,
                IsPercussion = s.IsPercussion,
                Bars = s.Bars.Select((b, bi) => new ExportBar
                {
                    Index = bi,
                    Voices = b.Voices.Select((v, vi) => new ExportVoice
                    {
                        Index = vi,
                        Beats = v.Beats.Select(be => new ExportBeat
                        {
                            Id = be.Id,
                            Index = be.Index,
                            DisplayStart = be.DisplayStart,
                            DisplayDuration = be.DisplayDuration,
                            PlaybackStart = be.PlaybackStart,
                            PlaybackDuration = be.PlaybackDuration,
                            Duration = be.Duration.ToString(),
                            IsRest = be.IsRest,
                            Dots = be.Dots,
                            HasTuplet = be.TupletNumerator > 0,
                            TupletNumerator = be.TupletNumerator,
                            TupletDenominator = be.TupletDenominator,
                            GraceType = be.GraceType.ToString(),
                            GraceIndex = be.GraceIndex,
                            IsLegatoOrigin = be.IsLegatoOrigin,
                            IsLegatoDestination = be.IsLegatoDestination,
                            IsLetRing = be.IsLetRing,
                            IsPalmMute = be.IsPalmMute,
                            Vibrato = be.Vibrato.ToString(),
                            Notes = be.Notes.Select(n => new ExportNote
                            {
                                String = n.String,
                                Fret = n.Fret,
                                IsTieOrigin = n.IsTieOrigin,
                                IsTieDestination = n.IsTieDestination,
                                IsHammerPullOrigin = n.IsHammerPullOrigin,
                                IsHammerPullDestination = n.IsHammerPullDestination,
                                IsSlurOrigin = n.IsSlurOrigin,
                                IsSlurDestination = n.IsSlurDestination,
                                IsGhost = n.IsGhost,
                                IsDead = n.IsDead,
                                IsPalmMute = n.IsPalmMute,
                                IsLetRing = n.IsLetRing,
                                IsStaccato = n.IsStaccato,
                                SlideInType = n.SlideInType.ToString(),
                                SlideOutType = n.SlideOutType.ToString(),
                                IsSlideOrigin = n.SlideTarget != null,
                                IsSlideDestination = n.SlideOrigin != null,
                                Vibrato = n.Vibrato.ToString(),
                                HarmonicType = n.HarmonicType.ToString(),
                                HarmonicValue = n.HarmonicValue,
                                BendType = n.BendType.ToString(),
                                BendPoints = n.BendPoints?.Select(bp => ((double)bp.Offset, (double)bp.Value)).ToList()
                                    ?? new List<(double, double)>(),
                                StringLow = n.String,
                                StringHigh = (s.Tuning?.Count ?? 6) - n.String + 1
                            }).ToList()
                        }).ToList()
                    }).ToList()
                }).ToList()
            }).ToList()
        }).ToList()
    };

    return Results.Json(export, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
});

app.Run();
