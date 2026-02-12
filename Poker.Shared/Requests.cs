namespace Poker.Shared;

public record JoinRoomRequest(string RoomId, string PlayerName);

public enum ActionType { Fold, Check, Call, Raise }

public record PlayerActionDto(string RoomId, Guid PlayerId, ActionType Action, int? Amount);
