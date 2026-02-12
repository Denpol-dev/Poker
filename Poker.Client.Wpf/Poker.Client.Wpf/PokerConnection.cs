using Microsoft.AspNetCore.SignalR.Client;
using Poker.Shared;

namespace Poker.Client.Wpf;

public sealed class PokerConnection
{
    public HubConnection Hub { get; }
    public Guid PlayerId { get; private set; } = Guid.Empty;
    public string RoomId { get; private set; } = "";

    public PokerConnection(string serverBaseUrl)
    {
        Hub = new HubConnectionBuilder()
            .WithUrl($"{serverBaseUrl.TrimEnd('/')}/poker")
            .WithAutomaticReconnect()
            .Build();

        Hub.On<string, Guid>("Joined", (roomId, playerId) =>
        {
            RoomId = roomId;
            PlayerId = playerId;
        });
    }

    public Task StartAsync() => Hub.StartAsync();

    public Task JoinRoomAsync(string roomId, string name)
        => Hub.InvokeAsync("JoinRoom", new JoinRoomRequest(roomId, name));

    public Task StartHandAsync(string roomId)
        => Hub.InvokeAsync("StartHand", roomId);

    public Task SendActionAsync(ActionType type, int? amount = null)
        => Hub.InvokeAsync("SendAction", new PlayerActionDto(RoomId, PlayerId, type, amount));
}
