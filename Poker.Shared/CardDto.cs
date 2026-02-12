namespace Poker.Shared;

public enum Suit { Hearts = 1, Diamonds = 2, Spades = 3, Clubs = 4 }
public enum Rank
{
    Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten,
    Jack = 11, Queen = 12, King = 13, Ace = 14
}

public readonly record struct CardDto(int Suit, int Rank, string Text);
