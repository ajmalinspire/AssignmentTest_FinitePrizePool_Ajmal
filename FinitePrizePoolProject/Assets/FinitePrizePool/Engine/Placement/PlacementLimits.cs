using System.Collections.Generic;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Maths;

namespace FinitePrizePool.Engine.Placement
{
    /// <summary>What a slot in a deal holds before tickets are merged in (§4.3 step 2).</summary>
    public enum SlotCategory
    {
        Loss,
        Regular,
        Big
    }

    /// <summary>
    /// The placement limits of §4.4 for one deal. A "gap" counts the spins strictly between two slots of the
    /// same kind; the start and the end of the deal count as boundaries ("including both ends").
    /// </summary>
    public sealed class PlacementLimits
    {
        public int MaxConsecutiveLosses;
        public int MaxRegularGap;
        public int MaxBigGap;
        public bool ForbidRepeatingInterval;

        /// <summary>
        /// Max consecutive losses = f × ⌈L ÷ (P + 1)⌉, max Regular gap = f × ⌈N ÷ (R + 1)⌉,
        /// max Big gap = f × ⌈N ÷ (B + 1)⌉, with f = 2 by default. An override replaces a formula.
        /// </summary>
        public static PlacementLimits For(DealPlan plan, PlacementSettings settings)
        {
            var factor = settings.LimitFactor;
            return new PlacementLimits
            {
                MaxConsecutiveLosses = settings.MaxConsecutiveLossesOverride
                                       ?? (int)(factor * IntMath.CeilDiv(plan.LosingTickets, plan.PrizeTickets + 1)),
                MaxRegularGap = settings.MaxRegularGapOverride
                                ?? (int)(factor * IntMath.CeilDiv(plan.PaidSpins, plan.RegularTickets + 1)),
                MaxBigGap = settings.MaxBigGapOverride
                            ?? (int)(factor * IntMath.CeilDiv(plan.PaidSpins, plan.BigTickets + 1)),
                ForbidRepeatingInterval = settings.ForbidRepeatingInterval
            };
        }

        /// <summary>
        /// Limits that no arrangement can meet, found before any attempt is made (§4.4). Each check asks whether
        /// the slots that are NOT of a kind can fit into the gaps that kind leaves, each gap at its maximum.
        /// </summary>
        public static List<string> FindImpossibleLimits(DealPlan plan, PlacementLimits limits)
        {
            var problems = new List<string>();
            long losses = plan.LosingTickets, prizes = plan.PrizeTickets, spins = plan.PaidSpins;

            if (losses > (prizes + 1) * limits.MaxConsecutiveLosses)
                problems.Add("max consecutive losses " + limits.MaxConsecutiveLosses + " is impossible: " + losses
                             + " losses cannot fit into " + (prizes + 1) + " gaps of at most " + limits.MaxConsecutiveLosses);

            if (spins - plan.RegularTickets > (plan.RegularTickets + 1) * limits.MaxRegularGap)
                problems.Add("max Regular gap " + limits.MaxRegularGap + " is impossible: " + (spins - plan.RegularTickets)
                             + " other spins cannot fit into " + (plan.RegularTickets + 1) + " gaps");

            if (spins - plan.BigTickets > (plan.BigTickets + 1) * limits.MaxBigGap)
                problems.Add("max Big gap " + limits.MaxBigGap + " is impossible: " + (spins - plan.BigTickets)
                             + " other spins cannot fit into " + (plan.BigTickets + 1) + " gaps");

            return problems;
        }

        public override string ToString()
        {
            return "max losses in a row " + MaxConsecutiveLosses + ", max Regular gap " + MaxRegularGap + ", max Big gap " + MaxBigGap;
        }
    }
}
