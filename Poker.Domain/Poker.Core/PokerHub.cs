using Microsoft.AspNetCore.SignalR;
using Poker.Shared;

namespace Poker.Core;

public class PokerHub(RoomRegistry rooms) : Hub
{
    public async Task JoinRoom(JoinRoomRequest req)
    {
        var room = rooms.GetOrCreate(req.RoomId);

        var playerId = Guid.NewGuid();

        lock (room)
        {
            room.ConnectionToPlayer[Context.ConnectionId] = playerId;

            room.Game.Players.Add(new PlayerState
            {
                PlayerId = playerId,
                Name = req.PlayerName,
                Stack = 1000
            });
        }

        Context.Items["roomId"] = req.RoomId;
        Context.Items["playerId"] = playerId;

        await Groups.AddToGroupAsync(Context.ConnectionId, req.RoomId);

        Console.WriteLine("Игрок" + req.PlayerName + " подключился к комнате " + req.RoomId);
        await Clients.Caller.SendAsync("Joined", req.RoomId, playerId);
        await BroadcastSnapshots(room, $"{req.PlayerName} joined");
        await SendPrivateSnapshotToCaller(room, playerId);
    }

    public async Task LeaveRoom(string roomId)
    {
        if (!rooms.TryGet(roomId, out var room)) return;

        Guid? pid = null;
        string? name = null;

        lock (room)
        {
            if (room.ConnectionToPlayer.TryGetValue(Context.ConnectionId, out var found))
            {
                pid = found;
                room.ConnectionToPlayer.Remove(Context.ConnectionId);

                var p = room.Game.Players.FirstOrDefault(x => x.PlayerId == pid.Value);
                if (p != null)
                {
                    name = p.Name;

                    if (IsHandInProgress(room.Game))
                    {
                        p.Status = PlayerStatus.Folded;
                        room.PendingRemoveAfterHand.Add(p.PlayerId);

                        if (room.Game.TurnIndex == room.Game.Players.IndexOf(p))
                            room.Game.TurnIndex = NextActiveIndex(room.Game, room.Game.TurnIndex);

                        TryFinishByEveryoneFolded(room.Game);
                    }
                    else
                    {
                        room.Game.Players.Remove(p);
                    }
                }

                FixTurnIndex(room.Game);
            }
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

        if (pid != null)
        {
            await BroadcastSnapshots(room, $"{name ?? "Player"} вышел");
            Console.WriteLine($"{name ?? "Player"} вышел");
        }
    }

    public async Task StartHand(string roomId)
    {
        if (!rooms.TryGet(roomId, out var room)) return;

        try
        {
            lock (room)
            {
                if (IsHandInProgress(room.Game))
                    throw new InvalidOperationException("Раздача уже идёт.");

                CleanupPendingRemovals(room);

                room.Game.ShowdownHands.Clear();
                room.Game.ShowdownReveal.Clear();
                room.Game.ShowdownMessage = null;

                GameEngine.StartHand(room);
            }

            await BroadcastSnapshots(room, "Рука открыта");
            await SendPrivateSnapshotsToAll(room);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task SendAction(PlayerActionDto dto)
    {
        if (!rooms.TryGet(dto.RoomId, out var room)) return;

        string msg;
        bool showdown;

        lock (room)
        {
            msg = GameEngine.ApplyAction(room, dto);

            showdown = room.Game.Street == Street.Showdown;

            if (showdown)
            {
                ResolveShowdown_NoSidePots_AndReveal(room);

                CleanupPendingRemovals(room);
            }
        }

        await BroadcastSnapshots(room, msg);

        if (showdown)
        {
            if (!string.IsNullOrWhiteSpace(room.Game.ShowdownMessage))
                await Clients.Group(dto.RoomId).SendAsync("Info", room.Game.ShowdownMessage);

            await BroadcastSnapshots(room, "Hand finished");
        }
    }

    private static void ResolveShowdown_NoSidePots_AndReveal(RoomState room)
    {
        var g = room.Game;

        var contenders = g.Players
            .Where(p => p.Status == PlayerStatus.Active)
            .ToList();

        g.ShowdownHands.Clear();
        g.ShowdownReveal.Clear();

        if (contenders.Count == 0)
        {
            g.ShowdownMessage = "Шоудаун: активных игроков нет.";
            return;
        }

        if (g.Board.Count < 5)
        {
            g.ShowdownMessage = $"Шоудаун: на борде недостаточно карт ({g.Board.Count}/5).";
            return;
        }

        var evaluated = new List<(PlayerState P, BestHand Best)>();

        foreach (var p in contenders)
        {
            if (p.Hole1 is null || p.Hole2 is null)
                continue;

            var seven = new List<Card>(7) { p.Hole1.Value, p.Hole2.Value };
            seven.AddRange(g.Board);

            var best = HandEvaluator.EvaluateBest(seven);
            evaluated.Add((p, best));
        }

        if (evaluated.Count == 0)
        {
            g.ShowdownMessage = "Шоудаун: нет рук для оценки.";
            return;
        }

        // Находим максимальную руку
        var bestValue = evaluated[0].Best.Value;
        for (int i = 1; i < evaluated.Count; i++)
            if (evaluated[i].Best.Value.CompareTo(bestValue) > 0)
                bestValue = evaluated[i].Best.Value;

        var winners = evaluated
            .Where(x => x.Best.Value.CompareTo(bestValue) == 0)
            .ToList();

        // Раздаём банк (без сайд-потов)
        int pot = g.Pot;
        if (pot > 0)
        {
            int share = pot / winners.Count;
            int remainder = pot % winners.Count;

            foreach (var w in winners)
                w.P.Stack += share;

            winners[0].P.Stack += remainder;

            g.Pot = 0;
        }

        // Заполняем reveal + текст рук (для Active игроков)
        foreach (var (P, Best) in evaluated)
        {
            g.ShowdownReveal.Add(P.PlayerId);

            var cat = ToRuCategory(Best.Value.Category);
            var bestCards = string.Join(" ", Best.Cards.Select(c => c.ToDto().Text));

            g.ShowdownHands[P.PlayerId] = $"{cat} ({bestCards})";
        }

        // Сообщение кто победил
        var winNames = string.Join(", ", winners.Select(x => x.P.Name));
        var winCatRu = ToRuCategory(bestValue.Category);

        g.ShowdownMessage = winners.Count == 1
            ? $"Победил {winNames}: {winCatRu}"
            : $"Делёж банка: {winNames}. Лучшая: {winCatRu}";
    }

    private static string ToRuCategory(HandCategory c) => c switch
    {
        HandCategory.HighCard => "Старшая карта",
        HandCategory.OnePair => "Пара",
        HandCategory.TwoPair => "Две пары",
        HandCategory.ThreeOfAKind => "Сет",
        HandCategory.Straight => "Стрит",
        HandCategory.Flush => "Флеш",
        HandCategory.FullHouse => "Фулл-хаус",
        HandCategory.FourOfAKind => "Каре",
        HandCategory.StraightFlush => "Стрит-флеш",
        _ => c.ToString()
    };

    private async Task BroadcastSnapshots(RoomState room, string? message)
    {
        var snap = BuildTableSnapshot(room, message);
        await Clients.Group(room.RoomId).SendAsync("TableSnapshot", snap);
    }

    private async Task SendPrivateSnapshotsToAll(RoomState room)
    {
        KeyValuePair<string, Guid>[] connections;
        lock (room)
        {
            connections = room.ConnectionToPlayer.ToArray();
        }

        foreach (var kv in connections)
        {
            var connId = kv.Key;
            var pid = kv.Value;

            var priv = BuildPrivateSnapshot(room, pid);
            await Clients.Client(connId).SendAsync("PrivateSnapshot", priv);
        }
    }

    private async Task SendPrivateSnapshotToCaller(RoomState room, Guid playerId)
    {
        var priv = BuildPrivateSnapshot(room, playerId);
        await Clients.Caller.SendAsync("PrivateSnapshot", priv);
    }

    private static TableSnapshotDto BuildTableSnapshot(RoomState room, string? message)
    {
        var g = room.Game;

        var board = g.Board.Select(c => c.ToDto()).ToArray();

        var players = g.Players.Select(p =>
        {
            CardDto? r1 = null;
            CardDto? r2 = null;
            string? hand = null;

            // на Showdown раскрываем только тех, кто дошёл (Active на момент вскрытия)
            if (g.Street == Street.Showdown && g.ShowdownReveal.Contains(p.PlayerId))
            {
                r1 = p.Hole1?.ToDto();
                r2 = p.Hole2?.ToDto();
                g.ShowdownHands.TryGetValue(p.PlayerId, out hand);
            }

            return new PlayerPublicDto(
                p.PlayerId,
                p.Name,
                p.Stack,
                p.BetThisStreet,
                p.Status.ToString(),
                p.Hole1 is null ? 0 : 2,
                r1,
                r2,
                hand
            );
        }).ToArray();

        var finalMessage = message;
        if (g.Street == Street.Showdown && !string.IsNullOrWhiteSpace(g.ShowdownMessage))
            finalMessage = g.ShowdownMessage;

        return new TableSnapshotDto(
            g.RoomId,
            g.HandId,
            g.Street.ToString(),
            g.Pot,
            g.CurrentBet,
            g.DealerIndex,
            g.TurnIndex,
            board,
            players,
            finalMessage
        );
    }

    private static PrivateSnapshotDto BuildPrivateSnapshot(RoomState room, Guid playerId)
    {
        var p = room.Game.Players.FirstOrDefault(x => x.PlayerId == playerId);
        if (p is null) return new PrivateSnapshotDto(playerId, null, null);

        return new PrivateSnapshotDto(
            playerId,
            p.Hole1?.ToDto(),
            p.Hole2?.ToDto()
        );
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var roomId = Context.Items.TryGetValue("roomId", out var r) ? r as string : null;
        var playerId = Context.Items.TryGetValue("playerId", out var v) ? v as Guid? : null;

        if (string.IsNullOrWhiteSpace(roomId) || playerId is null)
            return;

        if (!rooms.TryGet(roomId, out var room))
            return;

        string? name = null;
        bool didFold = false;

        lock (room)
        {
            room.ConnectionToPlayer.Remove(Context.ConnectionId);

            var p = room.Game.Players.FirstOrDefault(x => x.PlayerId == playerId.Value);
            if (p is null) return;

            name = p.Name;

            if (IsHandInProgress(room.Game))
            {
                p.Status = PlayerStatus.Folded;
                room.PendingRemoveAfterHand.Add(p.PlayerId);
                didFold = true;

                var idx = room.Game.Players.IndexOf(p);
                if (idx == room.Game.TurnIndex)
                    room.Game.TurnIndex = NextActiveIndex(room.Game, room.Game.TurnIndex);

                TryFinishByEveryoneFolded(room.Game);
            }
            else
            {
                room.Game.Players.Remove(p);
            }

            FixTurnIndex(room.Game);

            if (room.Game.Street == Street.Showdown)
                CleanupPendingRemovals(room);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
        await BroadcastSnapshots(room, didFold
            ? $"{name ?? "Player"} disconnected (auto-fold)"
            : $"{name ?? "Player"} disconnected");
    }

    // ===== helpers =====

    private static bool IsHandInProgress(GameState g)
        => g.Deck is not null && g.Street != Street.Showdown;

    private static void CleanupPendingRemovals(RoomState room)
    {
        if (room.PendingRemoveAfterHand.Count == 0) return;

        room.Game.Players.RemoveAll(p => room.PendingRemoveAfterHand.Contains(p.PlayerId));
        room.PendingRemoveAfterHand.Clear();

        FixTurnIndex(room.Game);
    }

    private static void FixTurnIndex(GameState g)
    {
        if (g.Players.Count == 0)
        {
            g.TurnIndex = 0;
            g.DealerIndex = 0;
            return;
        }

        if (g.TurnIndex < 0) g.TurnIndex = 0;
        g.TurnIndex %= g.Players.Count;

        if (g.DealerIndex < 0) g.DealerIndex = 0;
        g.DealerIndex %= g.Players.Count;
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

    private static void TryFinishByEveryoneFolded(GameState g)
    {
        var actives = g.Players.Where(p => p.Status == PlayerStatus.Active).ToList();
        if (actives.Count != 1) return;

        var winner = actives[0];
        winner.Stack += g.Pot;
        g.Pot = 0;
        g.Street = Street.Showdown;
    }
}
