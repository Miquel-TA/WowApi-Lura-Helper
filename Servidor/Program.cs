using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var connections = new ConcurrentDictionary<Guid, WebSocket>();
string[] _currentKeywords = Array.Empty<string>();

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var id = Guid.NewGuid();
    connections.TryAdd(id, ws);

    // Send current state immediately upon connection
    var initialMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_currentKeywords));
    await ws.SendAsync(new ArraySegment<byte>(initialMsg), WebSocketMessageType.Text, true, CancellationToken.None);

    var buffer = new byte[1024 * 4];

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            _currentKeywords = JsonSerializer.Deserialize<string[]>(message) ?? Array.Empty<string>();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Broadcast: [{string.Join(", ", _currentKeywords)}]");
            Console.ResetColor();

            // Broadcast to all other connected clients
            var broadcastMsg = Encoding.UTF8.GetBytes(message);
            foreach (var client in connections)
            {
                if (client.Value.State == WebSocketState.Open && client.Key != id)
                {
                    await client.Value.SendAsync(new ArraySegment<byte>(broadcastMsg), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
    }
    catch (WebSocketException) { /* Handle client disconnects gracefully */ }
    finally
    {
        connections.TryRemove(id, out _);
        if (ws.State != WebSocketState.Closed && ws.State != WebSocketState.Aborted)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
        }
    }
});

app.Run();