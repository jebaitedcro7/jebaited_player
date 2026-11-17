using JebaitedPlayer.Sync;

namespace JebaitedPlayer.Sync.Tests;

[TestClass]
public class CalibrationProbe
{
    [TestMethod]
    [TestCategory("Calibration")]
    public void Print_score_distribution()
    {
        var wrongG = new List<double>(); var wrongF = new List<double>();
        var rightG = new List<double>(); var rightF = new List<double>(); var errs = new List<double>();
        for (int seed = 0; seed < 40; seed++)
        {
            double minutes = 20 + seed % 5 * 20;
            var (speech, truth) = Film.Make(1000 + seed, minutes, vadNoise: 0.05 + seed % 3 * 0.05, musicShare: 0.1 + seed % 4 * 0.08);
            var (_, other) = Film.Make(5000 + seed, minutes);
            SyncResult w = SubtitleAligner.Align(speech, Film.Distort(other, 1, seed));
            wrongG.Add(w.GlobalScore); wrongF.Add(w.FinalScore);
            double off = (seed % 7 - 3) * 13.7;
            var subs = Film.Distort(truth, 1, off);
            SyncResult r = SubtitleAligner.Align(speech, subs);
            rightG.Add(r.GlobalScore); rightF.Add(r.FinalScore); errs.Add(Math.Abs(r.Offset - off));
            if (!r.IsUsable || w.IsUsable)
            {
                Console.WriteLine($"seed {seed} {minutes} min: right {AlignerTests.Describe(r)} | wrong {AlignerTests.Describe(w)}");
            }
        }

        Console.WriteLine($"KRIVI  global max {wrongG.Max():0.0} prosj {wrongG.Average():0.0} | final max {wrongF.Max():0.0} prosj {wrongF.Average():0.0}");
        Console.WriteLine($"PRAVI  global min {rightG.Min():0.0} prosj {rightG.Average():0.0} | final min {rightF.Min():0.0} prosj {rightF.Average():0.0}");
        Console.WriteLine($"Greška pomaka: max {errs.Max():0.000} s, prosj {errs.Average():0.000} s");
    }
}
