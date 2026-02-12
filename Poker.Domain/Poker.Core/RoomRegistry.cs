using Microsoft.Extensions.Logging;

namespace Poker.Core;

public sealed class RoomRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, RoomState> _rooms = new();

    public RoomState GetOrCreate(string roomId)
    {
        lock (_lock)
        {
            if (_rooms.TryGetValue(roomId, out var room)) return room;

            room = new RoomState
            {
                RoomId = roomId,
                Name = roomId,
            };
            room.Game.RoomId = roomId;

            _rooms[roomId] = room;
            Console.WriteLine("Комната создана: " + roomId);
            return room;
        }
    }

    public bool TryGet(string roomId, out RoomState room)
    {
        lock (_lock) return _rooms.TryGetValue(roomId, out room!);
    }

    public IEnumerable<RoomState> AllRooms() => _rooms.Values;
}
