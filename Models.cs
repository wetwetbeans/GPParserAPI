using AlphaTab.Model;
using System.Reflection;
using System.Text.Json;

namespace GuitarTabApi.Models
{
    // ------------------- Records -------------------
    public record ScoreJson(
        string Artist,
        string Title,
        string Album,
        string Subtitle,
        string Copyright,
        string MusicBy,
        string WordsBy,
        string Transcriber,
        string Instructions,
        string[] Notices,
        double Tempo,
        int TicksPerBeat,
        int[] GlobalTuning,
        string[] GlobalTuningText,
        string GlobalTuningLetters,
        TimeSigJson[] TimeSignatures,
        KeySigJson[] KeySignatures,
        TempoChangeJson[] TempoChanges,
        MarkerJson[] Markers,
        RepeatJson[] Repeats,
        TrackJson[] Tracks);

    public record TrackJson(string Name, int Program, int Channel, bool IsPercussion,
        int Capo, int Transpose, int Volume, int Pan, StaffJson[] Staves);

    public record StaffJson(int[] Tuning, string[] TuningText, string TuningLetters, BarJson[] Bars);

    public record BarJson(int Index, bool RepeatOpen, bool RepeatClose, int RepeatCount,
        int[] Voltas, int BarlineType, VoiceJson[] Voices,
        TimeSigJson? TimeSigOverride, MarkerJson? Marker);

    public record VoiceJson(BeatJson[] Beats);

    public record BeatJson(int Start, int Duration, int DurationSymbol, int Dots,
        (int Num, int Den) Tuplet, bool IsRest, int TremoloPicking, bool FadeIn,
        int Arpeggio, int BrushDirection, string WhammyBarPoints, NoteJson[] Notes);

    public record NoteJson(int StringLow, int StringHigh, int Fret, int PitchMidi,
        bool IsTieOrigin, bool IsTieDestination, bool IsGhost, bool IsDead,
        bool IsHarmonic, int HarmonicType, double HarmonicValue, bool IsPalmMute,
        bool IsLetRing, bool IsStaccato, bool IsHammerPullOrigin,
        bool IsHammerPullDestination, bool IsSlurOrigin, bool IsSlurDestination,
        int SlideInType, int SlideOutType, int BendType, BendPointJson[] BendPoints,
        int VibratoType, bool IsTrill, double TrillValue, int TrillSpeed,
        bool IsTapped, bool IsSlapped, bool IsPopped,
        int FingeringLeft, int FingeringRight);

    public record BendPointJson(double Offset, double Value);
    public record TimeSigJson(int Numerator, int Denominator, int BarIndex);
    public record KeySigJson(int Key, int Type, int BarIndex);
    public record TempoChangeJson(double Bpm, int BarIndex);
    public record MarkerJson(string Text, int ColorArgb, int BarIndex);
    public record RepeatJson(bool Open, bool Close, int Count, int BarIndex);

    // ------------------- Builder -------------------
    public static class ModelsBuilder
    {
        public static ScoreJson BuildScoreJson(Score score)
        {
            int tpb = Helpers.GetProp(score, "TicksPerBeat", 480);
            string[] noticesArr = string.IsNullOrWhiteSpace(score.Notices ?? "")
                ? Array.Empty<string>() : new[] { score.Notices! };

            return new ScoreJson(
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
                Array.Empty<TimeSigJson>(),
                Array.Empty<KeySigJson>(),
                Array.Empty<TempoChangeJson>(),
                Array.Empty<MarkerJson>(),
                Array.Empty<RepeatJson>(),
                Array.Empty<TrackJson>() // simplified for now
            );
        }
    }

    // ------------------- Helpers -------------------
    public static class Helpers
    {
        public static T GetProp<T>(object obj, string prop, T def)
        {
            var pi = obj.GetType().GetProperty(prop,
                BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.PropertyType == typeof(T))
            {
                return (T)(pi.GetValue(obj) ?? def);
            }
            return def;
        }
    }

    // ------------------- snake_case -------------------
    public class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var chars = new List<char>(name.Length * 2);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) chars.Add('_');
                    chars.Add(char.ToLower(c));
                }
                else
                {
                    chars.Add(c);
                }
            }
            return new string(chars.ToArray());
        }
    }
}
