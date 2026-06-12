using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var channels = new ConcurrentDictionary<string, ChannelState>();
var _validKeywords = new HashSet<string> { "té", "círculo", "triángulo", "equis", "rombo", "borrar" };
const int MaxPayloadSize = 2048;

var _options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

var cleanupTimer = new Timer(_ =>
{
    var now = DateTime.UtcNow;
    foreach (var entry in channels)
    {
        var channel = entry.Value;

        // Auto-clear symbols after 15 seconds of inactivity
        if (channel.Keywords.Length > 0 && (now - channel.LastUpdate).TotalSeconds > 15)
        {
            channel.Keywords = Array.Empty<string>();
            var broadcastMsg = Encoding.UTF8.GetBytes("[]");

            var tasks = channel.Connections.Values
                .Where(c => c.State == WebSocketState.Open)
                .Select(async client =>
                {
                    try { await client.SendAsync(new ArraySegment<byte>(broadcastMsg), WebSocketMessageType.Text, true, CancellationToken.None); } catch { }
                });
            Task.WhenAll(tasks);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Cleanup] Auto-cleared symbols for channel: {entry.Key}");
        }

        // Delete the channel entirely if it's empty and unused for 5 minutes
        if (channel.Connections.IsEmpty && (now - channel.LastUpdate).TotalMinutes > 5)
        {
            channels.TryRemove(entry.Key, out ChannelState? _);
        }
    }
}, null, 5000, 5000);

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var channelId = context.Request.Query["token"].ToString();


    if (string.IsNullOrEmpty(channelId) || channelId.Length > 30 || !channelId.All(char.IsLetterOrDigit))
    {
        context.Response.StatusCode = 400;
        return;
    }

    var channel = channels.GetOrAdd(channelId, _ => new ChannelState());

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var id = Guid.NewGuid();
    channel.Connections.TryAdd(id, ws);

    var initialMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(channel.Keywords));
    await ws.SendAsync(new ArraySegment<byte>(initialMsg), WebSocketMessageType.Text, true, CancellationToken.None);

    var buffer = new byte[1024];
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                ms.Write(buffer, 0, result.Count);

                if (ms.Length > MaxPayloadSize)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Payload too large", CancellationToken.None);
                    return;
                }
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            var message = Encoding.UTF8.GetString(ms.ToArray());
            string[]? parsedInput = null;

            try { parsedInput = JsonSerializer.Deserialize<string[]>(message, _options); } catch { /* Ignore malformed JSON */ }

            if (parsedInput != null)
            {
                if (parsedInput.Length == 1 && parsedInput[0] == "ping")
                {
                    var pongMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { "pong" }));
                    try { await ws.SendAsync(new ArraySegment<byte>(pongMsg), WebSocketMessageType.Text, true, CancellationToken.None); } catch { }
                    continue;
                }

                if (parsedInput.Length <= 5)
                {
                    channel.LastUpdate = DateTime.UtcNow;
                    channel.Keywords = parsedInput.Where(k => _validKeywords.Contains(k.ToLower())).ToArray();

                    var broadcastMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(channel.Keywords));

                    var tasks = channel.Connections
                        .Where(c => c.Value.State == WebSocketState.Open)
                        .Select(async client =>
                        {
                            try { await client.Value.SendAsync(new ArraySegment<byte>(broadcastMsg), WebSocketMessageType.Text, true, CancellationToken.None); }
                            catch { }
                        });

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Channel: {channelId} | Symbols: [{string.Join(", ", channel.Keywords)}]");
                    await Task.WhenAll(tasks);
                }
            }
        }
    }
    catch (Exception) { /* Network drops handled gracefully */ }
    finally
    {
        channel.Connections.TryRemove(id, out _);
        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None); } catch { }
        }
    }
});

app.Run();

public class ChannelState
{
    public string[] Keywords { get; set; } = Array.Empty<string>();
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    public ConcurrentDictionary<Guid, WebSocket> Connections { get; } = new();
}