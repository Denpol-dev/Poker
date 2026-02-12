namespace Poker.Shared;

public readonly record struct PlayerPublicDto(
    Guid PlayerId,
    string Name,
    int Stack,
    int BetThisStreet,
    string Status,
    int HoleCardsCount,

    CardDto? RevealHole1,
    CardDto? RevealHole2,

    string? ShowdownHand
);


public record TableSnapshotDto(
    string RoomId,
    int HandId,
    string Street,
    int Pot,
    int CurrentBet,
    int DealerIndex,
    int TurnIndex,
    CardDto[] Board,
    PlayerPublicDto[] Players,
    string? Message
);

public record PrivateSnapshotDto(
    Guid PlayerId,
    CardDto? Hole1,
    CardDto? Hole2
);
