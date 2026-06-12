using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var channels = new ConcurrentDictionary<string, ChannelState>();
var _validKeywords = new HashSet<string> { "té", "círculo", "triángulo", "equis", "rombo", "borrar" };

var cleanupTimer = new Timer(_ =>
{
    var now = DateTime.UtcNow;
    foreach (var entry in channels)
    {
        if ((now - entry.Value.LastUpdate).TotalSeconds > 30)
        {
            channels.TryRemove(entry.Key, out ChannelState? _);
            Console.WriteLine($"[Cleanup] Removed inactive channel: {entry.Key}");
        }
    }
}, null, 10000, 10000);

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var channelId = context.Request.Query["token"].ToString();
    if (string.IsNullOrEmpty(channelId)) { context.Response.StatusCode = 401; return; }

    var channel = channels.GetOrAdd(channelId, _ => new ChannelState());

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var id = Guid.NewGuid();
    channel.Connections.TryAdd(id, ws);

    var initialMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(channel.Keywords));
    await ws.SendAsync(new ArraySegment<byte>(initialMsg), WebSocketMessageType.Text, true, CancellationToken.None);

    var buffer = new byte[1024 * 4];
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
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            var message = Encoding.UTF8.GetString(ms.ToArray());
            var parsedInput = JsonSerializer.Deserialize<string[]>(message);

            if (parsedInput != null && parsedInput.Length <= 5)
            {
                channel.Keywords = parsedInput.Where(k => _validKeywords.Contains(k.ToLower())).ToArray();
                channel.LastUpdate = DateTime.UtcNow;

                var broadcastMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(channel.Keywords));
                
                var tasks = channel.Connections
                    .Where(c => c.Value.State == WebSocketState.Open)
                    .Select(async client => await client.Value.SendAsync(new ArraySegment<byte>(broadcastMsg), WebSocketMessageType.Text, true, CancellationToken.None));

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Channel: {channelId} | Symbols: [{string.Join(", ", channel.Keywords)}]");

                await Task.WhenAll(tasks);
            }
        }
    }
    catch (Exception)
    {
        // Connection errors
    }
    finally
    {
        channel.Connections.TryRemove(id, out _);

        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
        {
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
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
