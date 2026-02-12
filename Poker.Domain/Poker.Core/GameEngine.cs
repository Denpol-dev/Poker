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
                    var amount = dto.Amount ?? 0;
                    if (amount <= g.CurrentBet) return "При поднятии ставки общая сумма ставки должна превышать текущую ставку.";

                    int need = amount - p.BetThisStreet;
                    if (need <= 0) return "Неверная сумма поднятия, нужно больше.";

                    TakeChips(p, g, need);
                    g.CurrentBet = amount;

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

    private static void AdvanceTurnOrStreet(GameState g)
    {
        // следующий активный
        g.TurnIndex = NextActiveIndex(g, g.TurnIndex);

        // проверим, завершилась ли улица: все активные либо уравняли, либо all-in (all-in пока нет)
        if (IsStreetComplete(g))
        {
            // собрать ставки в банк
            foreach (var p in g.Players) { /* уже в Pot при TakeChips */ }
            foreach (var p in g.Players) p.BetThisStreet = 0;
            g.CurrentBet = 0;

            // открыть следующую улицу
            if (g.Deck is null) throw new InvalidOperationException("Deck missing.");

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
            g.LastAggressorIndex = -1;
            g.ActionsSinceLastRaise = 0;
            // первый ход на новой улице: после дилера (MVP)
            g.TurnIndex = NextActiveIndex(g, g.DealerIndex);
        }
    }

    private static bool IsStreetComplete(GameState g)
    {
        // 1) Все активные уравняли текущую ставку
        foreach (var p in g.Players)
        {
            if (p.Status != PlayerStatus.Active) continue;
            if (p.BetThisStreet != g.CurrentBet) return false;
        }

        // 2) Все активные успели сходить после последнего raise (или начала улицы)
        int activeCount = g.Players.Count(p => p.Status == PlayerStatus.Active);
        if (activeCount <= 1) return true;

        return g.ActionsSinceLastRaise >= activeCount;
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
