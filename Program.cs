using System.IO;
using System.Text.Json;
using AlphaTab.Io;
using AlphaTab.Model;
using AlphaTab.Importer;
using AlphaTab.Core.EcmaScript;
using GPParser.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string API_KEY = "13"; // simple API key check

app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue("x-api-key", out var key) || key != API_KEY)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }
    await next();
});

app.MapPost("/parse", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected multipart/form-data with a file field");
    }

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded");
    }

    Score score;
    using (var ms = new MemoryStream())
    {
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var u8 = new Uint8Array(bytes);
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
        TicksPerBeat = 960, // ✅ AlphaTab 1.6.1 uses fixed 960
        Tracks = score.Tracks.Select((t, ti) => new ExportTrack
        {
            Index = ti,
            Name = t.Name ?? "",
            ShortName = t.ShortName ?? "",
            PlaybackInfo = new ExportPlaybackInfo
            {
                Program = t.PlaybackInfo?.Program ?? 0,
                Volume = t.PlaybackInfo?.Volume ?? 100,
            },
            Staves = t.Staves.Select((s, si) => new ExportStaff
            {
                Index = si,
                Tuning = s.Tuning?.Select(x => (double)x).ToList() ?? new(),
                TuningName = s.TuningName ?? "",
                Capo = s.Capo,
                TranspositionPitch = s.TranspositionPitch,
                IsPercussion = s.IsPercussion,
                Bars = s.Bars.Select((b, bi) => new ExportBar
                {
                    Index = bi,
                    Voices = b.Voices.Select((v, vi) => new ExportVoice
                    {
                        Index = vi,
                        Beats = v.Beats.Select((be, bei) => new ExportBeat
                        {
                            Id = be.Id,
                            Index = be.Index,
                            DisplayStart = be.DisplayStart,
                            DisplayDuration = be.DisplayDuration,
                            PlaybackStart = be.PlaybackStart,
                            PlaybackDuration = be.PlaybackDuration,
                            Duration = be.Duration.ToString(),
                            IsRest = be.IsRest,
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
                                StringLow = n.String,
                                StringHigh = (s.Tuning?.Count ?? 6) - n.String + 1,
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
                                BendPoints = n.BendPoints?.Select(bp =>
                                    (Offset: (double)bp.Offset, Value: (double)bp.Value)).ToList()
        ?? new List<(double, double)>()
                            }).ToList()

                        }).ToList()
                    }).ToList()
                }).ToList()
            }).ToList()
        }).ToList()
    };

    return Results.Json(export, new JsonSerializerOptions
    {
        WriteIndented = true
    });
});

app.Run();
