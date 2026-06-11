using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

string[] _currentKeywords = Array.Empty<string>();
object _stateLock = new object();
const string ApiKey = "TokenSeguro123";

app.MapGet("/read", () =>
{
    lock (_stateLock) return Results.Ok(_currentKeywords);
});

app.MapPost("/write", ([FromHeader(Name = "X-Auth-Token")] string token, [FromBody] string[] keywords) =>
{
    if (token != ApiKey) return Results.Unauthorized();

    lock (_stateLock)
    {
        _currentKeywords = keywords ?? Array.Empty<string>();

        // Log en el terminal
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[{(DateTime.Now).ToLongTimeString()}] Recibido y actualizado: [{string.Join(", ", _currentKeywords)}]");
        Console.ResetColor();
    }

    return Results.Ok();
});

app.Run();