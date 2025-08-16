//using System.Collections.Generic;
//using AlphaTab.Model;

//public class ScoreJson
//{
//    public string Title { get; set; } = "";
//    public string Artist { get; set; } = "";
//    public double Tempo { get; set; }
//    public string TempoLabel { get; set; } = "";
//    public List<MasterBarJson> MasterBars { get; set; } = new();
//    public List<TrackJson> Tracks { get; set; } = new();

//    public static ScoreJson ToJson(Score score)
//    {
//        var s = new ScoreJson
//        {
//            Title = score.Title,
//            Artist = score.Artist,
//            Tempo = score.Tempo,
//            TempoLabel = score.TempoLabel
//        };

//        foreach (var mb in score.MasterBars)
//            s.MasterBars.Add(MasterBarJson.ToJson(mb));

//        foreach (var t in score.Tracks)
//            s.Tracks.Add(TrackJson.ToJson(t));

//        return s;
//    }
//}

//public class MasterBarJson
//{
//    public int Index { get; set; }
//    public int Numerator { get; set; }
//    public int Denominator { get; set; }
//    public string TripletFeel { get; set; } = "";
//    public bool IsRepeatStart { get; set; }
//    public bool IsRepeatEnd { get; set; }
//    public int RepeatCount { get; set; }

//    public static MasterBarJson ToJson(MasterBar mb)
//    {
//        return new MasterBarJson
//        {
//            Index = (int)mb.Index,
//            Numerator = (int)mb.TimeSignatureNumerator,
//            Denominator = (int)mb.TimeSignatureDenominator,
//            TripletFeel = mb.TripletFeel.ToString(),
//            IsRepeatStart = mb.IsRepeatStart,
//            IsRepeatEnd = mb.IsRepeatEnd,
//            RepeatCount = (int)mb.RepeatCount
//        };
//    }
//}

//public class TrackJson
//{
//    public int Index { get; set; }
//    public string Name { get; set; } = "";
//    public int Staves { get; set; }

//    public static TrackJson ToJson(Track t)
//    {
//        return new TrackJson
//        {
//            Index = (int)t.Index,
//            Name = t.Name,
//            Staves = t.Staves.Count
//        };
//    }
//}
