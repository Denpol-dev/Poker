using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Poker.Shared;

namespace Poker.Client.Wpf;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // ====== Ввод пользователя ======
    public string ServerUrl { get; set; } = "http://localhost:5000";
    public string RoomId { get; set; } = "room";
    public string PlayerName { get; set; } = "Name";

    // ====== Состояние стола ======
    private int _handId;
    public int HandId { get => _handId; set => Set(ref _handId, value); }

    private string _street = "-";
    public string Street { get => _street; set => Set(ref _street, value); }

    private int _pot;
    public int Pot { get => _pot; set => Set(ref _pot, value); }

    private int _currentBet;
    public int CurrentBet { get => _currentBet; set => Set(ref _currentBet, value); }

    private int _turnIndex;
    public int TurnIndex { get => _turnIndex; set => Set(ref _turnIndex, value); }

    private string _message = "";
    public string Message { get => _message; set => Set(ref _message, value); }

    // ====== Игроки ======
    public ObservableCollection<PlayerVm> Players { get; } = new();

    // ====== Карты (для UI с карточками) ======
    public ObservableCollection<CardVm> BoardCards { get; } = new();
    public ObservableCollection<CardVm> MyHoleCards { get; } = new();

    // ====== (опционально) старые текстовые поля ======
    private string _boardText = "-";
    public string BoardText { get => _boardText; set => Set(ref _boardText, value); }

    private string _myCards = "?? ??";
    public string MyCards { get => _myCards; set => Set(ref _myCards, value); }

    // ====== Лог ======
    public ObservableCollection<string> Log { get; } = new();

    // ====== Идентичность ======
    public Guid? MyPlayerId { get; set; }

    private bool _isMyTurn;
    public bool IsMyTurn { get => _isMyTurn; set => Set(ref _isMyTurn, value); }

    // ===== helpers =====
    private static bool IsRedSuit(int suit)
        => suit == (int)Suit.Hearts || suit == (int)Suit.Diamonds;

    private static CardVm ToCardVm(CardDto dto)
        => new() { Text = dto.Text, IsRed = IsRedSuit(dto.Suit) };

    private static CardVm ToUnknownCardVm()
        => new() { Text = "??", IsRed = false };

    public void ApplyTableSnapshot(TableSnapshotDto snap)
    {
        HandId = snap.HandId;
        Street = snap.Street;
        Pot = snap.Pot;
        CurrentBet = snap.CurrentBet;
        TurnIndex = snap.TurnIndex;

        Message = snap.Message ?? "";

        // Board
        BoardCards.Clear();
        foreach (var c in snap.Board)
            BoardCards.Add(ToCardVm(c));

        BoardText = (snap.Board.Length == 0) ? "-" : string.Join(" ", snap.Board.Select(c => c.Text));

        // Players
        Players.Clear();
        for (int i = 0; i < snap.Players.Length; i++)
        {
            var p = snap.Players[i];

            CardVm? r1 = p.RevealHole1 is null ? null : ToCardVm(p.RevealHole1.Value);
            CardVm? r2 = p.RevealHole2 is null ? null : ToCardVm(p.RevealHole2.Value);

            Players.Add(new PlayerVm
            {
                PlayerId = p.PlayerId,
                Name = p.Name,
                Stack = p.Stack,
                BetThisStreet = p.BetThisStreet,
                Status = p.Status,
                HoleCardsCount = p.HoleCardsCount,

                IsTurn = (i == snap.TurnIndex),
                IsMe = (MyPlayerId is not null && p.PlayerId == MyPlayerId.Value),

                Reveal1 = r1,
                Reveal2 = r2,
                ShowdownHand = p.ShowdownHand
            });
        }

        IsMyTurn = Players.Any(x => x.IsMe && x.IsTurn);

        if (!string.IsNullOrWhiteSpace(snap.Message))
            AddLog($"[Стол] {snap.Message}");
    }

    public void ApplyPrivateSnapshot(PrivateSnapshotDto snap)
    {
        MyHoleCards.Clear();

        if (snap.Hole1 is not null) MyHoleCards.Add(ToCardVm(snap.Hole1.Value));
        else MyHoleCards.Add(ToUnknownCardVm());

        if (snap.Hole2 is not null) MyHoleCards.Add(ToCardVm(snap.Hole2.Value));
        else MyHoleCards.Add(ToUnknownCardVm());

        var c1 = snap.Hole1?.Text ?? "??";
        var c2 = snap.Hole2?.Text ?? "??";
        MyCards = $"{c1} {c2}";

        AddLog($"[Мои карты] {MyCards}");
    }

    public void AddLog(string text)
    {
        Log.Add(text);
        while (Log.Count > 200) Log.RemoveAt(0);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? prop = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}

public sealed class PlayerVm
{
    public Guid PlayerId { get; set; }

    public string Name { get; set; } = "";
    public int Stack { get; set; }
    public int BetThisStreet { get; set; }
    public string Status { get; set; } = "";
    public int HoleCardsCount { get; set; }

    public bool IsTurn { get; set; }
    public bool IsMe { get; set; }

    // Showdown / reveal
    public CardVm? Reveal1 { get; set; }
    public CardVm? Reveal2 { get; set; }
    public string? ShowdownHand { get; set; }
}

public sealed class CardVm
{
    public string Text { get; set; } = "??";
    public bool IsRed { get; set; }
}
