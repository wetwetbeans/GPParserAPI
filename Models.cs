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
        public int TicksPerBeat { get; set; }
        public List<ExportTrack> Tracks { get; set; } = new();
    }

    public sealed class ExportMasterBar
    {
        public double Index { get; set; }
        public double Start { get; set; }
        public double Duration { get; set; }
        public int TimeSignatureNumerator { get; set; } = 4;
        public int TimeSignatureDenominator { get; set; } = 4;
        public bool IsFreeTime { get; set; }
        public bool IsDoubleBar { get; set; }
        public bool IsRepeatStart { get; set; }
        public bool IsRepeatEnd { get; set; }
        public int RepeatCount { get; set; }
        public bool IsAnacrusis { get; set; }
        public double? TempoBpm { get; set; }
        public string? SectionName { get; set; }
        public List<ExportVoice> Voices { get; set; } = new();
    }

    public sealed class ExportTrack
    {
        public double Index { get; set; }
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
        public double Index { get; set; }
        public List<double> Tuning { get; set; } = new();
        public string TuningName { get; set; } = string.Empty;
        public double Capo { get; set; }
        public double TranspositionPitch { get; set; }
        public bool IsPercussion { get; set; }
        public List<ExportBar> Bars { get; set; } = new();
    }

    public class ExportBar
    {
        public double Index { get; set; }
        public List<ExportVoice> Voices { get; set; } = new();
    }

    public class ExportVoice
    {
        public double Index { get; set; }
        public List<ExportBeat> Beats { get; set; } = new();
    }
    public class ExportBeat
    {
        private static double _globalBeatId;

        // --- Identity ---
        public double Id { get; set; } = _globalBeatId++;
        public double Index { get; set; }

        // --- Timing ---
        public double DisplayStart { get; set; }
        public double DisplayDuration { get; set; }
        public double PlaybackStart { get; set; }
        public double PlaybackDuration { get; set; }

        // --- Beat Type ---
        public string Duration { get; set; } = "Quarter";
        public bool IsRest { get; set; }
        public double Dots { get; set; }
        public bool IsEmpty { get; set; }
        public bool IsSlashed { get; set; }

        // --- Tuplet ---
        public double TupletNumerator { get; set; } = -1;
        public double TupletDenominator { get; set; } = -1;
        public bool HasTuplet { get; set; }

        // --- Grace ---
        public string GraceType { get; set; } = "None";
        public double GraceIndex { get; set; } = -1;
        public bool IsGrace => GraceType != "None" && GraceIndex >= 0;

        // --- Text / Lyrics ---
        public string? Text { get; set; }
        public List<string>? Lyrics { get; set; }

        // --- Legato ---
        public bool IsLegatoOrigin { get; set; }
        public bool IsLegatoDestination { get; set; }

        // --- Styles ---
        public bool IsLetRing { get; set; }
        public bool IsPalmMute { get; set; }
        public bool DeadSlapped { get; set; }
        public bool Slapped { get; set; }
        public bool Popped { get; set; }
        public bool Tapped { get; set; }
        public bool FadeIn { get; set; }

        // --- Techniques ---
        public string Vibrato { get; set; } = "None";
        public bool HasSlide { get; set; }
        public bool HasBend { get; set; }

        // --- Dynamics ---
        public string Dynamics { get; set; } = "F"; // forte default

        // --- Chords ---
        public string? ChordId { get; set; }
        public bool HasChord { get; set; }

        // --- Notes ---
        public List<ExportNote> Notes { get; set; } = new();
    }


    public class ExportNote
    {
        public double String { get; set; }
        public double Fret { get; set; }
        public bool IsTieOrigin { get; set; }
        public bool IsTieDestination { get; set; }
        public bool IsHammerPullOrigin { get; set; }
        public bool IsHammerPullDestination { get; set; }
        public bool IsSlurOrigin { get; set; }
        public bool IsSlurDestination { get; set; }
        public bool IsGhost { get; set; }
        public bool IsDead { get; set; }
        public bool IsPalmMute { get; set; }
        public bool IsLetRing { get; set; }
        public bool IsStaccato { get; set; }
        public string SlideInType { get; set; } = "None";
        public string SlideOutType { get; set; } = "None";
        public string Vibrato { get; set; } = "None";
        public string HarmonicType { get; set; } = "None";
        public double HarmonicValue { get; set; }
        public string BendType { get; set; } = "None";
        public List<(double Offset, double Value)> BendPoints { get; set; } = new();
    }
}
