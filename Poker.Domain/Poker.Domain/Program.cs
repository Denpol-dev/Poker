using Poker.Core;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:5000");

builder.Services.AddSignalR();
builder.Services.AddSingleton<RoomRegistry>();

var app = builder.Build();

app.MapHub<PokerHub>("/poker");

app.Run();
