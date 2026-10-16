using System.Collections.Generic;
using System.Linq;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Placement;

namespace FinitePrizePool.Engine.Building
{
    public sealed class VerificationResult
    {
        public readonly List<string> Problems = new List<string>();
        public long WagerCents;
        public long PayoutCents;

        public bool IsValid => Problems.Count == 0;
        public Rtp Rtp => new Rtp(PayoutCents, WagerCents);
    }

    /// <summary>
    /// Re-checks a saved deal from its tickets alone (§4.3 step 5): counts, every award, cycle isolation,
    /// placement limits, and that wagers and payouts give exactly the target RTP once the deal is used up.
    /// A deal that fails is never played.
    /// </summary>
    public static class DealVerifier
    {
        public static VerificationResult Verify(DealDocument deal)
        {
            var result = new VerificationResult();
            var problems = result.Problems;
            var tickets = deal.Tickets ?? new List<Ticket>();

            if (tickets.Count != deal.PaidSpins)
                problems.Add("deal has " + tickets.Count + " tickets, expected " + deal.PaidSpins);

            for (var i = 0; i < tickets.Count; i++)
            {
                if (tickets[i].Position != i)
                {
                    problems.Add("ticket " + i + " is stored at position " + tickets[i].Position);
                    break;
                }
            }

            CheckAwards(deal, tickets, problems);
            CheckCycles(deal, tickets, SlotCategory.Regular, Bag.FromEntries("Regular", deal.RegularBag), deal.RegularTickets, problems);
            CheckCycles(deal, tickets, SlotCategory.Big, Bag.FromEntries("Big", deal.BigBag), deal.BigTickets, problems);

            var placement = PlacementValidator.Validate(tickets.Select(t => t.Category).ToList(), deal.Limits,
                deal.RegularTickets, deal.BigTickets, deal.LosingTickets);
            problems.AddRange(placement.Violations.Select(v => "placement: " + v));

            result.WagerCents = checked(tickets.Count * deal.BetCents);
            result.PayoutCents = tickets.Sum(t => t.AwardCents);
            CheckRtp(deal, result);
            return result;
        }

        private static void CheckAwards(DealDocument deal, List<Ticket> tickets, List<string> problems)
        {
            foreach (var ticket in tickets)
            {
                if (ticket.Category == SlotCategory.Loss)
                {
                    if (ticket.Kind != PrizeKind.Loss || ticket.AwardCents != 0 || ticket.Cycle != -1)
                        problems.Add("loss slot " + ticket.Position + " carries a prize");
                    continue;
                }

                if (ticket.Kind == PrizeKind.Loss)
                {
                    problems.Add("prize slot " + ticket.Position + " holds no prize");
                    continue;
                }

                if (!ticket.Prize.TryGetAwardCents(deal.BetCents, deal.BonusUnitCents, out var expected) || expected != ticket.AwardCents)
                    problems.Add("ticket " + ticket.Position + " pays " + Money.Format(ticket.AwardCents) + ", expected " + Money.Format(expected));
            }
        }

        /// <summary>
        /// Cycle isolation: read in play order, the tickets of one category must come cycle 0, then cycle 1, and
        /// so on, and each cycle must hold exactly one copy of its bag. So no ticket of cycle n+1 is played
        /// before every ticket of cycle n.
        /// </summary>
        private static void CheckCycles(DealDocument deal, List<Ticket> tickets, SlotCategory category, Bag bag,
            long expectedCount, List<string> problems)
        {
            var stream = tickets.Where(t => t.Category == category).ToList();
            if (stream.Count != expectedCount) return; // already reported by the placement check

            for (var i = 0; i < stream.Count; i++)
            {
                var expectedCycle = i / bag.TicketCount;
                if (stream[i].Cycle == expectedCycle) continue;
                problems.Add(category + " ticket at position " + stream[i].Position + " is from cycle " + stream[i].Cycle
                             + " but cycle " + expectedCycle + " is not finished");
                return;
            }

            var expected = Count(bag.Tickets);
            for (var cycle = 0; cycle < deal.PairedCycles; cycle++)
            {
                var actual = Count(stream.Skip(cycle * bag.TicketCount).Take(bag.TicketCount).Select(t => t.Prize));
                if (SameCounts(expected, actual)) continue;
                problems.Add(category + " cycle " + cycle + " does not hold exactly one copy of the " + bag.Name + " bag");
                return;
            }
        }

        private static void CheckRtp(DealDocument deal, VerificationResult result)
        {
            if (result.WagerCents != deal.PlannedWagerCents || result.PayoutCents != deal.PlannedPayoutCents)
            {
                result.Problems.Add("tickets wager " + Money.Format(result.WagerCents) + " and pay " + Money.Format(result.PayoutCents)
                                    + ", the plan said " + Money.Format(deal.PlannedWagerCents) + " and " + Money.Format(deal.PlannedPayoutCents));
            }

            var rtp = result.Rtp;
            if (deal.RtpMode == RtpMode.Exact)
            {
                if (!rtp.IsExactly(deal.TargetRtpBasisPoints))
                    result.Problems.Add("RTP is " + rtp.Format(6) + ", not exactly " + Rtp.FormatBasisPoints(deal.TargetRtpBasisPoints));
                return;
            }

            var low = (deal.TargetRtpBasisPoints - deal.ToleranceBasisPoints) / 100m;
            var high = (deal.TargetRtpBasisPoints + deal.ToleranceBasisPoints) / 100m;
            if (rtp.Percent < low || rtp.Percent > high)
                result.Problems.Add("RTP " + rtp.Format(6) + " is outside the tolerance band " + low + "%-" + high + "%");
        }

        private static Dictionary<PrizeDefinition, int> Count(IEnumerable<PrizeDefinition> prizes)
        {
            var counts = new Dictionary<PrizeDefinition, int>();
            foreach (var prize in prizes)
            {
                counts.TryGetValue(prize, out var n);
                counts[prize] = n + 1;
            }
            return counts;
        }

        private static bool SameCounts(Dictionary<PrizeDefinition, int> a, Dictionary<PrizeDefinition, int> b)
        {
            return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var n) && n == pair.Value);
        }
    }
}
