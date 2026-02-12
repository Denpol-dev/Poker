using Poker.Shared;

namespace Poker.Core;

public enum Street { Preflop, Flop, Turn, River, Showdown }
public enum PlayerStatus { Active, Folded, AllIn, Out }

public readonly record struct Card(Suit Suit, Rank Rank)
{
    public CardDto ToDto() => new((int)Suit, (int)Rank, $"{RankToString(Rank)}{SuitToChar(Suit)}");

    private static string RankToString(Rank r) => r switch
    {
        Rank.Ten => "10",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        Rank.Ace => "A",
        _ => ((int)r).ToString()
    };

    private static char SuitToChar(Suit s) => s switch
    {
        Suit.Hearts => '♥',
        Suit.Diamonds => '♦',
        Suit.Spades => '♠',
        Suit.Clubs => '♣',
        _ => '?'
    };
}

public sealed class Deck
{
    private readonly List<Card> _cards;
    private int _idx;

    private Deck(List<Card> cards) { _cards = cards; _idx = 0; }

    public static Deck CreateShuffled(Random rng)
    {
        var cards = new List<Card>(52);
        foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                cards.Add(new Card(suit, rank));

        // Fisher–Yates
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        return new Deck(cards);
    }

    public Card Draw()
    {
        if (_idx >= _cards.Count) throw new InvalidOperationException("Deck empty");
        return _cards[_idx++];
    }
}

public sealed class PlayerState
{
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";
    public int Stack { get; set; } = 1000;

    public int BetThisStreet { get; set; } = 0;
    public PlayerStatus Status { get; set; } = PlayerStatus.Active;

    public Card? Hole1 { get; set; }
    public Card? Hole2 { get; set; }
}

public sealed class GameState
{
    public string RoomId { get; set; } = "";
    public int HandId { get; set; } = 0;

    public Street Street { get; set; } = Street.Preflop;

    public List<Card> Board { get; } = new();
    public int Pot { get; set; } = 0;

    public int DealerIndex { get; set; } = 0;
    public int TurnIndex { get; set; } = 0;

    public int CurrentBet { get; set; } = 0;
    public int SmallBlind { get; set; } = 10;
    public int BigBlind { get; set; } = 20;

    public int LastAggressorIndex { get; set; } = -1;
    public int ActionsSinceLastRaise { get; set; } = 0;

    public Dictionary<Guid, string> ShowdownHands { get; } = new();
    public HashSet<Guid> ShowdownReveal { get; } = new();
    public string? ShowdownMessage { get; set; }

    public List<PlayerState> Players { get; } = new();
    public Deck? Deck { get; set; }

    public void ResetStreetBets()
    {
        CurrentBet = 0;
        foreach (var p in Players) p.BetThisStreet = 0;
    }
}

public sealed class RoomState
{
    public string RoomId { get; init; } = "";
    public string Name { get; init; } = "";
    public Dictionary<string, Guid> ConnectionToPlayer { get; } = new(); // connId -> playerId
    public GameState Game { get; } = new();
    public Random Rng { get; } = new Random(); // один RNG на комнату
    public HashSet<Guid> PendingRemoveAfterHand { get; } = [];
}
