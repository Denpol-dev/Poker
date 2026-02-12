using Poker.Shared;

namespace Poker.Core;

public static class HandEvaluator
{
    public static BestHand EvaluateBest(IReadOnlyList<Card> seven)
    {
        if (seven.Count != 7)
            throw new ArgumentException("Need exactly 7 cards");

        BestHand? best = null;

        foreach (var combo in CombinationsOf5(seven))
        {
            var hv = Evaluate5(combo);
            var cur = new BestHand(hv, combo);

            if (best is null)
            {
                best = cur;
                continue;
            }

            int cmp = cur.Value.CompareTo(best.Value);

            if (cmp > 0 || (cmp == 0 && CompareCards(cur.Cards, best.Cards) > 0))
            {
                best = cur;
            }
        }

        return best!;
    }

    private static int CompareCards(IReadOnlyList<Card> a, IReadOnlyList<Card> b)
    {
        var ar = a.Select(c => (int)c.Rank).OrderByDescending(x => x).ToArray();
        var br = b.Select(c => (int)c.Rank).OrderByDescending(x => x).ToArray();

        for (int i = 0; i < 5; i++)
        {
            if (ar[i] != br[i])
                return ar[i].CompareTo(br[i]);
        }
        return 0;
    }

    private static IEnumerable<IReadOnlyList<Card>> CombinationsOf5(IReadOnlyList<Card> c)
    {
        // 7 choose 5 = 21
        for (int a = 0; a < 3; a++)
            for (int b = a + 1; b < 4; b++)
                for (int d = b + 1; d < 5; d++)
                    for (int e = d + 1; e < 6; e++)
                        for (int f = e + 1; f < 7; f++)
                            yield return new[] { c[a], c[b], c[d], c[e], c[f] };
    }

    public static HandValue Evaluate5(IReadOnlyList<Card> five)
    {
        var ranks = five.Select(c => (int)c.Rank).OrderByDescending(x => x).ToArray();
        var suits = five.Select(c => (int)c.Suit).ToArray();

        bool isFlush = suits.All(s => s == suits[0]);
        bool isStraight = TryGetStraightHigh(ranks, out int straightHigh);

        var groups = ranks
            .GroupBy(x => x)
            .Select(g => new { Rank = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Rank)
            .ToList();

        if (isFlush && isStraight)
            return new HandValue(HandCategory.StraightFlush, new[] { straightHigh });

        if (groups[0].Count == 4)
            return new HandValue(HandCategory.FourOfAKind,
                new[] { groups[0].Rank, groups[1].Rank });

        if (groups[0].Count == 3 && groups[1].Count == 2)
            return new HandValue(HandCategory.FullHouse,
                new[] { groups[0].Rank, groups[1].Rank });

        if (isFlush)
            return new HandValue(HandCategory.Flush, ranks);

        if (isStraight)
            return new HandValue(HandCategory.Straight, new[] { straightHigh });

        if (groups[0].Count == 3)
        {
            int trips = groups[0].Rank;
            var kickers = groups.Skip(1).Select(g => g.Rank).OrderByDescending(x => x).ToArray();
            return new HandValue(HandCategory.ThreeOfAKind, new[] { trips }.Concat(kickers).ToArray());
        }

        if (groups[0].Count == 2 && groups[1].Count == 2)
        {
            int highPair = Math.Max(groups[0].Rank, groups[1].Rank);
            int lowPair = Math.Min(groups[0].Rank, groups[1].Rank);
            int kicker = groups[2].Rank;
            return new HandValue(HandCategory.TwoPair, new[] { highPair, lowPair, kicker });
        }

        if (groups[0].Count == 2)
        {
            int pair = groups[0].Rank;
            var kickers = groups.Skip(1).Select(g => g.Rank).OrderByDescending(x => x).ToArray();
            return new HandValue(HandCategory.OnePair, new[] { pair }.Concat(kickers).ToArray());
        }

        return new HandValue(HandCategory.HighCard, ranks);
    }

    private static bool TryGetStraightHigh(int[] ranksDesc, out int high)
    {
        high = 0;
        var uniq = ranksDesc.Distinct().OrderByDescending(x => x).ToArray();
        if (uniq.Length < 5) return false;

        // обычный стрит
        for (int i = 0; i <= uniq.Length - 5; i++)
        {
            int start = uniq[i];
            bool ok = true;
            for (int k = 1; k < 5; k++)
            {
                if (!uniq.Contains(start - k)) { ok = false; break; }
            }
            if (ok) { high = start; return true; }
        }

        // wheel A-2-3-4-5 => high=5
        if (uniq.Contains(14) && uniq.Contains(5) && uniq.Contains(4) && uniq.Contains(3) && uniq.Contains(2))
        {
            high = 5;
            return true;
        }

        return false;
    }
}

public enum HandCategory
{
    HighCard = 1,
    OnePair = 2,
    TwoPair = 3,
    ThreeOfAKind = 4,
    Straight = 5,
    Flush = 6,
    FullHouse = 7,
    FourOfAKind = 8,
    StraightFlush = 9
}

public readonly record struct HandValue(
    HandCategory Category,
    int[] Kickers
) : IComparable<HandValue>
{
    public int CompareTo(HandValue other)
    {
        int c = Category.CompareTo(other.Category);
        if (c != 0) return c;

        int n = Math.Min(Kickers.Length, other.Kickers.Length);
        for (int i = 0; i < n; i++)
        {
            if (Kickers[i] != other.Kickers[i])
                return Kickers[i].CompareTo(other.Kickers[i]);
        }
        return 0;
    }
}

public sealed record BestHand(HandValue Value, IReadOnlyList<Card> Cards);
