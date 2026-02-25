using Poker.Shared;

namespace Poker.Core;

public static class GameEngine
{
    public static void StartHand(RoomState room)
    {
        var g = room.Game;
        if (g.Players.Count < 2) throw new InvalidOperationException("Need at least 2 players.");

        g.HandId++;
        g.Board.Clear();
        g.Pot = 0;

        // сброс статусов
        foreach (var p in g.Players)
        {
            p.Status = p.Stack > 0 ? PlayerStatus.Active : PlayerStatus.Out;
            p.BetThisStreet = 0;
            p.Hole1 = null;
            p.Hole2 = null;
        }

        g.Deck = Deck.CreateShuffled(room.Rng);
        g.Street = Street.Preflop;

        // сдача по 2
        foreach (var p in g.Players.Where(p => p.Status == PlayerStatus.Active))
        {
            p.Hole1 = g.Deck.Draw();
            p.Hole2 = g.Deck.Draw();
        }

        // дилер/блайнды/очередь (MVP: простое вращение)
        g.DealerIndex = (g.DealerIndex + 1) % g.Players.Count;

        int sbIndex = NextActiveIndex(g, g.DealerIndex);
        int bbIndex = NextActiveIndex(g, sbIndex);

        g.LastAggressorIndex = bbIndex;  // блайнд — это “ставка”, считаем агрессором BB
        g.ActionsSinceLastRaise = 0;

        PostBlind(g, sbIndex, g.SmallBlind);
        PostBlind(g, bbIndex, g.BigBlind);

        g.CurrentBet = g.BigBlind;

        // первый ход: после BB
        g.TurnIndex = NextActiveIndex(g, bbIndex);
    }

    public static string ApplyAction(RoomState room, PlayerActionDto dto)
    {
        var g = room.Game;
        if (g.Players.Count < 2) return "Нет игроков.";

        var idx = g.Players.FindIndex(p => p.PlayerId == dto.PlayerId);
        if (idx < 0) return "Unknown player.";
        if (idx != g.TurnIndex) return "Не твой ход.";

        var p = g.Players[idx];
        if (p.Status != PlayerStatus.Active) return "Игрок не активен.";

        int toCall = Math.Max(0, g.CurrentBet - p.BetThisStreet);

        switch (dto.Action)
        {
            case ActionType.Fold:
                p.Status = PlayerStatus.Folded;
                break;

            case ActionType.Check:
                if (toCall != 0) return "Чек запрещен, нужно ответить/сбросить/поднять.";
                break;

            case ActionType.Call:
                if (toCall == 0) break;
                TakeChips(p, g, toCall);
                break;

            case ActionType.Raise:
                {
                    var target = dto.Amount ?? 0;

                    // максимум, до которого он может довести общую ставку (total)
                    var maxTotal = p.BetThisStreet + p.Stack;

                    if (target > maxTotal)
                        target = maxTotal; // превращаем в all-in

                    if (target <= g.CurrentBet)
                    {
                        // это не рейз, это all-in "колл насколько смог"
                        int need = g.CurrentBet - p.BetThisStreet;
                        if (need > 0) TakeChips(p, g, need);
                        break;
                    }

                    int needRaise = target - p.BetThisStreet;
                    TakeChips(p, g, needRaise);

                    g.CurrentBet = target;

                    g.LastAggressorIndex = idx;
                    g.ActionsSinceLastRaise = 1;
                    break;
                }

            default:
                return "Unknown action.";
        }

        // если остался один активный — победа без вскрытия
        if (ActivePlayers(g).Count == 1)
        {
            var winner = ActivePlayers(g)[0];
            winner.Stack += g.Pot;
            var msg = $"{winner.Name} победил (все сбросили). Выигрыш={g.Pot}";
            g.Pot = 0;
            g.Street = Street.Showdown;
            return msg;
        }

        g.ActionsSinceLastRaise++;

        // переход хода / улиц
        AdvanceTurnOrStreet(g);
        return $"{p.Name}: {dto.Action}" + (dto.Amount is null ? "" : $" {dto.Amount}");
    }

    private static bool NoOneCanAct(GameState g)
    => g.Players.All(p => p.Status is PlayerStatus.Folded or PlayerStatus.Out or PlayerStatus.AllIn);

    private static void DealRestToShowdown(GameState g)
    {
        if (g.Deck is null) throw new InvalidOperationException("Deck missing.");

        while (g.Street != Street.Showdown)
        {
            switch (g.Street)
            {
                case Street.Preflop:
                    g.Board.Add(g.Deck.Draw());
                    g.Board.Add(g.Deck.Draw());
                    g.Board.Add(g.Deck.Draw());
                    g.Street = Street.Flop;
                    break;
                case Street.Flop:
                    g.Board.Add(g.Deck.Draw());
                    g.Street = Street.Turn;
                    break;
                case Street.Turn:
                    g.Board.Add(g.Deck.Draw());
                    g.Street = Street.River;
                    break;
                case Street.River:
                    g.Street = Street.Showdown;
                    break;
            }
        }
    }

    private static void AdvanceTurnOrStreet(GameState g)
    {
        if (NoOneCanAct(g))
        {
            DealRestToShowdown(g);
            return;
        }

        g.TurnIndex = NextActiveIndex(g, g.TurnIndex);

        if (IsStreetComplete(g))
        {
            foreach (var p in g.Players)
                p.BetThisStreet = 0;

            g.CurrentBet = 0;
            g.LastAggressorIndex = -1;
            g.ActionsSinceLastRaise = 0;

            if (g.Deck is null)
                throw new InvalidOperationException("Deck missing.");

            switch (g.Street)
            {
                case Street.Preflop:
                    g.Board.Add(g.Deck.Draw());
                    g.Board.Add(g.Deck.Draw());
                    g.Board.Add(g.Deck.Draw());
                    g.Street = Street.Flop;
                    break;
                case Street.Flop:
                    g.Board.Add(g.Deck.Draw());
                    g.Street = Street.Turn;
                    break;
                case Street.Turn:
                    g.Board.Add(g.Deck.Draw());
                    g.Street = Street.River;
                    break;
                case Street.River:
                    g.Street = Street.Showdown;
                    break;
            }
            if (g.Street != Street.Showdown)
                g.TurnIndex = NextActiveIndex(g, g.DealerIndex);
        }

        if (NoOneCanAct(g))
        {
            DealRestToShowdown(g);
            return;
        }
    }

    private static bool IsStreetComplete(GameState g)
    {
        // 1) Кто в раздаче (не Folded/Out)
        foreach (var p in g.Players)
        {
            if (p.Status is PlayerStatus.Folded or PlayerStatus.Out) continue;

            // Active должен уравнять
            if (p.Status == PlayerStatus.Active && p.BetThisStreet != g.CurrentBet)
                return false;

            // AllIn может НЕ уравнять
        }

        // 2) Все, кто может действовать (Active), успели сходить после last raise
        int canActCount = g.Players.Count(p => p.Status == PlayerStatus.Active);
        if (canActCount <= 1) return true;

        return g.ActionsSinceLastRaise >= canActCount;
    }

    private static void PostBlind(GameState g, int index, int blind)
    {
        var p = g.Players[index];
        if (p.Status != PlayerStatus.Active) return;
        TakeChips(p, g, blind);
    }

    private static void TakeChips(PlayerState p, GameState g, int amount)
    {
        var a = Math.Min(amount, p.Stack);
        p.Stack -= a;
        p.BetThisStreet += a;
        g.Pot += a;

        if (p.Stack == 0 && p.Status == PlayerStatus.Active)
            p.Status = PlayerStatus.AllIn;
    }

    private static int NextActiveIndex(GameState g, int fromIndex)
    {
        int n = g.Players.Count;
        for (int step = 1; step <= n; step++)
        {
            int i = (fromIndex + step) % n;
            if (g.Players[i].Status == PlayerStatus.Active) return i;
        }
        return fromIndex;
    }

    private static List<PlayerState> ActivePlayers(GameState g)
        => g.Players.Where(p => p.Status == PlayerStatus.Active).ToList();
}
