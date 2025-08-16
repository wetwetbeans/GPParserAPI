//using System;
//using System.IO;
//using System.Text.Json;
//using AlphaTab;
//using AlphaTab.Model;
//using AlphaTab.Importer;

//class Program
//{
//    static void Main(string[] args)
//    {
//        if (args.Length == 0)
//        {
//            Console.WriteLine("Usage: dotnet run -- <file.gp3/gp4/gp5/gpx/gp>");
//            return;
//        }

//        string filePath = args[0];
//        if (!File.Exists(filePath))
//        {
//            Console.WriteLine($"File not found: {filePath}");
//            return;
//        }

//        try
//        {
//            // Read file into bytes
//            byte[] data = File.ReadAllBytes(filePath);

//            // ✅ Load score
//            Score score = ScoreLoader.LoadScoreFromBytes(data, new Settings());

//            // ✅ Convert Score → ScoreJson (your Models.cs contains ToJson methods)
//            var scoreJson = ScoreJson.ToJson(score);

//            // Pretty print JSON
//            var options = new JsonSerializerOptions { WriteIndented = true };
//            string output = JsonSerializer.Serialize(scoreJson, options);

//            Console.WriteLine(output);
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine("Error: " + ex);
//        }
//    }
//}
