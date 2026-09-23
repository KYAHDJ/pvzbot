using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Media;
using System.Reflection;
using System.Threading;

namespace PvZController;

// Renders Windows speech locally, amplifies it, then plays it off the UI/game thread.
internal sealed class VoicePersona : IDisposable
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _speaker;
    private readonly object _gate = new();
    private DateTime _nextLine = DateTime.MinValue;
    private int _pending;
    private bool _disposed;

    internal event Action<string>? Speaking;

    internal VoicePersona()
    {
        _speaker = new Thread(SpeakLoop) { IsBackground = true, Name = "PvZ local voice" };
        _speaker.SetApartmentState(ApartmentState.STA);
        _speaker.Start();
    }

    internal bool TrySay(string line, bool urgent = false)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > 180) return false;
        lock (_gate)
        {
            if (_disposed || DateTime.UtcNow < _nextLine || _pending >= 2) return false;
            // 5-10 sec quiet gap after each completed line; urgent events get shorter gap but still no overlap.
            _nextLine = DateTime.UtcNow.AddSeconds(urgent ? 5.5 : 8.0);
            _pending++;
            _lines.Enqueue(line);
            _wake.Set();
            return true;
        }
    }

    private void SpeakLoop()
    {
        object? voice = null;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null) return;
            voice = Activator.CreateInstance(type);
            if (voice is null) return;
            type.InvokeMember("Rate", BindingFlags.SetProperty, null, voice, [-1]);
            type.InvokeMember("Volume", BindingFlags.SetProperty, null, voice, [100]);
            while (true)
            {
                _wake.WaitOne();
                while (_lines.TryDequeue(out var line))
                {
                    try { RenderAndPlay(type, voice, line); }
                    catch { /* Audio trouble must never interrupt PvZ. */ }
                    lock (_gate) _pending--;
                }
                lock (_gate) { if (_disposed) break; }
            }
        }
        catch { /* Local speech may be unavailable on some PCs. */ }
        finally
        {
            if (voice is not null && System.Runtime.InteropServices.Marshal.IsComObject(voice))
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(voice);
        }
    }

    private void RenderAndPlay(Type voiceType, object voice, string line)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pvz-voice-{Guid.NewGuid():N}.wav");
        object? fileStream = null;
        try
        {
            var streamType = Type.GetTypeFromProgID("SAPI.SpFileStream")
                ?? throw new InvalidOperationException("Windows speech file output is unavailable.");
            fileStream = Activator.CreateInstance(streamType)
                ?? throw new InvalidOperationException("Windows speech file output could not start.");
            streamType.InvokeMember("Open", BindingFlags.InvokeMethod, null, fileStream, [path, 3, false]);
            voiceType.InvokeMember("AudioOutputStream", BindingFlags.SetProperty, null, voice, [fileStream]);
            voiceType.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, [line]);
            streamType.InvokeMember("Close", BindingFlags.InvokeMethod, null, fileStream, []);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(fileStream);
            fileStream = null;

            var wave = File.ReadAllBytes(path);
            AmplifyPcm16(wave);
            Speaking?.Invoke(line);
            using var audio = new MemoryStream(wave, writable: false);
            using var player = new SoundPlayer(audio);
            player.PlaySync();
        }
        finally
        {
            if (fileStream is not null && System.Runtime.InteropServices.Marshal.IsComObject(fileStream))
            {
                try { fileStream.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, fileStream, []); } catch { }
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(fileStream);
            }
            try { File.Delete(path); } catch { }
        }
    }

    private static void AmplifyPcm16(byte[] wave)
    {
        if (wave.Length < 44 || wave[0] != 'R' || wave[1] != 'I' || wave[2] != 'F' || wave[3] != 'F') return;
        var span = wave.AsSpan();
        var format = 0;
        var bits = 0;
        var dataStart = 0;
        var dataLength = 0;
        for (var offset = 12; offset + 8 <= wave.Length;)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset + 4, 4));
            if (size < 0 || offset + 8L + size > wave.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(wave, offset, 4);
            if (tag == "fmt " && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset + 8, 2));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset + 22, 2));
            }
            if (tag == "data") { dataStart = offset + 8; dataLength = size; break; }
            offset += 8 + size + (size & 1);
        }
        if (format != 1 || bits != 16 || dataLength < 2) return;
        var ceiling = Math.Tanh(2.5);
        for (var i = dataStart; i + 1 < dataStart + dataLength; i += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i, 2));
            var boosted = Math.Tanh(sample / 32768.0 * 2.5) / ceiling;
            var value = (short)Math.Clamp(Math.Round(boosted * 32767), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(i, 2), value);
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _wake.Set();
        if (_speaker.Join(1000)) _wake.Dispose();
    }
}
