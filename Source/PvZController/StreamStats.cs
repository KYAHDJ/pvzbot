using System.IO;
using System.Text.Json;

namespace PvZController;

// Loss count, best time and run number. Saved to stats.json so a restart keeps them.
internal sealed class StreamStats
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "stats.json");
    private sealed record Saved(int Losses, double BestSeconds, int Runs);

    internal int Losses { get; private set; }
    internal double BestSeconds { get; private set; }
    internal int Runs { get; private set; }
    internal double LastRunSeconds { get; private set; }
    private DateTime _runStart = DateTime.MinValue;

    internal StreamStats()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) is { } s)
                (Losses, BestSeconds, Runs) = (s.Losses, s.BestSeconds, s.Runs);
        }
        catch { }
    }

    internal bool InRun => _runStart != DateTime.MinValue;
    internal double RunSeconds => InRun ? (DateTime.UtcNow - _runStart).TotalSeconds : 0;

    internal void BeginRun()
    {
        if (InRun) return;
        _runStart = DateTime.UtcNow;
        Runs++;
        Save();
    }

    // Returns true when the finished run is a new best.
    internal bool EndRunAsLoss()
    {
        if (!InRun) return false;
        var seconds = RunSeconds;
        LastRunSeconds = seconds;
        _runStart = DateTime.MinValue;
        Losses++;
        var best = seconds > BestSeconds;
        if (best) BestSeconds = seconds;
        Save();
        return best;
    }

    internal static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    internal string Summary => $"Bot lost: {Losses}   Time: {Clock(RunSeconds)}   Best: {Clock(BestSeconds)}";

    private void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(Losses, BestSeconds, Runs))); }
        catch { }
    }
}
