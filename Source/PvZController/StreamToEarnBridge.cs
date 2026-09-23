using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PvZController;

internal sealed record StreamEffect(string Id, JsonElement Payload);

internal sealed class StreamToEarnBridge : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    internal event Action<StreamEffect>? EffectReceived;
    internal bool IsRunning => _listener is not null;

    internal void Start()
    {
        if (_listener is not null) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 8082);
        _listener.Start();
        _ = AcceptLoop(_cts.Token);
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = Handle(client, token);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(250, token).ConfigureAwait(false); }
        }
    }

    private async Task Handle(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var headerBytes = new List<byte>();
                var lastFour = new Queue<byte>(4);
                while (headerBytes.Count < 65536)
                {
                    var one = new byte[1];
                    if (await stream.ReadAsync(one, token) == 0) return;
                    headerBytes.Add(one[0]);
                    if (lastFour.Count == 4) lastFour.Dequeue();
                    lastFour.Enqueue(one[0]);
                    if (lastFour.SequenceEqual(new byte[] { 13,10,13,10 })) break;
                }
                var header = Encoding.ASCII.GetString(headerBytes.ToArray());
                var first = header.Split("\r\n", StringSplitOptions.None)[0].Split(' ');
                var method = first.ElementAtOrDefault(0) ?? "";
                var path = first.ElementAtOrDefault(1) ?? "/";
                var lengthLine = header.Split("\r\n").FirstOrDefault(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                var length = lengthLine is null ? 0 : int.Parse(lengthLine.Split(':')[1].Trim());
                var bodyBytes = new byte[Math.Clamp(length, 0, 1_000_000)];
                await stream.ReadExactlyAsync(bodyBytes, token);

                if (method == "OPTIONS") { await Respond(stream, 200, "", token); return; }
                if (path == "/version") { await Respond(stream, 200, "{\"version\":1}", token); return; }
                if (path == "/trigger_effect" && method == "POST")
                {
                    using var doc = JsonDocument.Parse(bodyBytes);
                    if (!doc.RootElement.TryGetProperty("effect_id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                    { await Respond(stream, 400, "Invalid or missing 'effect_id'", token); return; }
                    EffectReceived?.Invoke(new StreamEffect(idElement.GetString()!, doc.RootElement.Clone()));
                    await Respond(stream, 200, "", token);
                    return;
                }
                await Respond(stream, 404, "", token);
            }
            catch { try { await Respond(stream, 400, "Invalid JSON format", token); } catch { } }
        }
    }

    private static async Task Respond(NetworkStream stream, int status, string body, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var text = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nAccess-Control-Allow-Origin: https://app.streamtoearn.io\r\nAccess-Control-Allow-Headers: Content-Type\r\nAccess-Control-Allow-Methods: POST, OPTIONS\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text), token);
        if (payload.Length > 0) await stream.WriteAsync(payload, token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }
}
