using System.Collections.Generic;

namespace GPParser.Models
{
    public class ExportScore
    {
        public string Album { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Copyright { get; set; } = "";
        public string Title { get; set; } = "";
        public string Words { get; set; } = "";
        public double Tempo { get; set; } = 120.0;
        public int TicksPerBeat { get; set; } = 960;

        // ✅ Score-level timeline of time signatures
        public List<ExportTimeSignature> TimeSignatures { get; set; } = new();

        public List<ExportMasterBar> MasterBars { get; set; } = new();
        public List<ExportTrack> Tracks { get; set; } = new();
    }

    public class ExportTimeSignature
    {
        public int BarIndex { get; set; }
        public double Numerator { get; set; }
        public double Denominator { get; set; }

        public ExportTimeSignature(int barIndex, double num, double den)
        {
            BarIndex = barIndex;
            Numerator = num;
            Denominator = den;
        }
    }

    public sealed class ExportMasterBar
    {
        public int Index { get; set; }
        public double Start { get; set; }
        public double Duration { get; set; }
        public double TimeSignatureNumerator { get; set; } = 4;
        public double TimeSignatureDenominator { get; set; } = 4;
        public bool IsFreeTime { get; set; }
        public bool IsDoubleBar { get; set; }
        public bool IsRepeatStart { get; set; }
        public bool IsRepeatEnd { get; set; }
        public double RepeatCount { get; set; }
        public bool IsAnacrusis { get; set; }
        public double? TempoBpm { get; set; }
        public string? SectionName { get; set; }
        public List<ExportVoice> Voices { get; set; } = new();
    }

    public sealed class ExportTrack
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public ExportPlaybackInfo PlaybackInfo { get; set; } = new();
        public List<ExportStaff> Staves { get; set; } = new();
    }

    public sealed class ExportPlaybackInfo
    {
        public double Program { get; set; }
        public double Volume { get; set; }
    }

    public class ExportStaff
    {
        public int Index { get; set; }
        public List<int> Tuning { get; set; } = new(); // ✅ stays int
        public string TuningName { get; set; } = string.Empty;
        public int Capo { get; set; }
        public int TranspositionPitch { get; set; }
        public bool IsPercussion { get; set; }
        public List<ExportBar> Bars { get; set; } = new();
    }

    public class ExportBar
    {
        public int Index { get; set; }
        public List<ExportVoice> Voices { get; set; } = new();
    }

    public class ExportVoice
    {
        public int Index { get; set; }
        public List<ExportBeat> Beats { get; set; } = new();
    }

    public class ExportBeat
    {
        private static int _globalBeatId;

        // Identity
        public double Id { get; set; } = _globalBeatId++;
        public double Index { get; set; }
        public double BarIndex { get; set; }

        // Timing
        public double DisplayStart { get; set; }
        public double DisplayDuration { get; set; }
        public double PlaybackStart { get; set; }
        public double PlaybackDuration { get; set; }

        // Beat type
        public int Duration { get; set; }
        public bool IsRest { get; set; }
        public double Dots { get; set; }
        public bool IsEmpty { get; set; }
        public bool IsSlashed { get; set; }

        // Tuplet
        public double TupletNumerator { get; set; } = -1;
        public double TupletDenominator { get; set; } = -1;
        public bool HasTuplet { get; set; }

        // Grace
        public string GraceType { get; set; } = "None";
        public double GraceIndex { get; set; } = -1;

        // Text / lyrics
        public string? Text { get; set; }
        public List<string>? Lyrics { get; set; }

        // Legato
        public bool IsLegatoOrigin { get; set; }
        public bool IsLegatoDestination { get; set; }

        // Styles
        public bool IsLetRing { get; set; }
        public bool IsPalmMute { get; set; }
        public bool FadeIn { get; set; }

        // Techniques
        public string Vibrato { get; set; } = "None";
        public bool HasSlide { get; set; }
        public bool HasBend { get; set; }

        // Dynamics
        public string Dynamics { get; set; } = "F";

        // Chords
        public string? ChordId { get; set; }
        public bool HasChord { get; set; }

        // Notes
        public List<ExportNote> Notes { get; set; } = new();
    }

    public class ExportNote
    {
        public double String { get; set; }
        public double Fret { get; set; }

        // Ties & techniques
        public bool IsTieOrigin { get; set; }
        public bool IsTieDestination { get; set; }
        public bool IsHammerPullOrigin { get; set; }
        public bool IsHammerPullDestination { get; set; }
        public bool IsSlurOrigin { get; set; }
        public bool IsSlurDestination { get; set; }

        // Styles
        public bool IsGhost { get; set; }
        public bool IsDead { get; set; }
        public bool IsPalmMute { get; set; }
        public bool IsLetRing { get; set; }
        public bool IsStaccato { get; set; }

        // Slides
        public int SlideInType { get; set; }
        public int SlideOutType { get; set; }
        public bool IsSlideOrigin { get; set; }
        public bool IsSlideDestination { get; set; }

        // Vibrato & harmonics
        public string Vibrato { get; set; } = "None";
        public string HarmonicType { get; set; } = "None";
        public double HarmonicValue { get; set; }

        // Bends
        public string BendType { get; set; } = "None";
        public List<(double Offset, double Value)> BendPoints { get; set; } = new();

        // Extra string info
        public double StringHigh { get; set; }
        public double StringLow { get; set; }
    }
}
