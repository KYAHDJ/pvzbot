using System.Diagnostics;
using System.IO;
using System.Text;

namespace PvZController;

// Keeps names and comments clean enough to say out loud on a live stream.
internal static class ChatFilter
{
    private static readonly string[] Blocked =
    [
        "fuck", "shit", "bitch", "cunt", "dick", "cock", "pussy", "asshole", "bastard", "slut", "whore",
        "nigg", "fag", "retard", "tranny", "rape", "rapist", "molest", "pedo", "kys", "killyourself",
        "nazi", "hitler", "porn", "sex", "nude", "suicide", "cum", "penis", "vagina", "boob", "dildo",
        "hoe", "twat", "wank", "terroris", "isis", "kkk",
    ];

    private static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.ToLowerInvariant())
        {
            var c = ch switch { '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '5' => 's', '7' => 't', '@' => 'a', '$' => 's', '!' => 'i', _ => ch };
            if (char.IsLetter(c) && c < 128) sb.Append(c);
        }
        return sb.ToString();
    }

    internal static bool IsDirty(string text)
    {
        var n = Normalize(text);
        return Blocked.Any(n.Contains);
    }

    // Speakable name: plain ASCII, short, never a bad word. Falls back to "chat".
    internal static string CleanName(string nickname, string handle)
    {
        foreach (var raw in new[] { nickname, handle })
        {
            var sb = new StringBuilder();
            foreach (var ch in raw ?? "")
                if (ch is >= ' ' and <= '~' && ch != '[' && ch != ']' && ch != '{' && ch != '}') sb.Append(ch);
            var name = sb.ToString().Trim().Trim('_', '.', '-', ' ');
            name = name.Replace('_', ' ').Replace('.', ' ');
            if (name.Length > 16) name = name[..16].Trim();
            if (name.Length < 2 || !name.Any(char.IsLetter) || IsDirty(name)) continue;
            return name;
        }
        return "chat";
    }

    internal static string CleanText(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text ?? "")
            if (ch is >= ' ' and <= '~' && ch != '[' && ch != '{' && ch != '}') sb.Append(ch);
        return sb.ToString().Trim();
    }
}

// Starts chat\chat_bridge.py (reads TikTok live chat) and restarts it if it ever exits.
internal sealed class ChatBridgeLauncher : IDisposable
{
    private Process? _process;
    private bool _disposed;
    private DateTime _nextStart = DateTime.MinValue;
    private bool _warnedNoPython;
    internal event Action<string>? Message;

    internal void Tick()
    {
        if (_disposed || DateTime.UtcNow < _nextStart) return;
        if (_process is { HasExited: false }) return;
        _nextStart = DateTime.UtcNow.AddSeconds(30);
        var script = Path.Combine(AppContext.BaseDirectory, "chat", "chat_bridge.py");
        if (!File.Exists(script)) return;
        try
        {
            var psi = new ProcessStartInfo("python")
            {
                WorkingDirectory = Path.GetDirectoryName(script)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(script);
            _process = Process.Start(psi);
            if (_process is null) return;
            _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Message?.Invoke(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            Message?.Invoke("CHAT helper started.");
        }
        catch
        {
            if (!_warnedNoPython) { _warnedNoPython = true; Message?.Invoke("CHAT helper could not start: Python is not installed or not on PATH."); }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try { if (_process is { HasExited: false }) _process.Kill(true); } catch { }
        _process?.Dispose();
    }
}
