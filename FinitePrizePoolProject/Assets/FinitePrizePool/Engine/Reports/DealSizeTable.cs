using System.Collections.Generic;
using System.Linq;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;
using FinitePrizePool.Engine.Placement;

namespace FinitePrizePool.Engine.Reports
{
    /// <summary>One row of the deal-size table (§4.2): a plan plus the placement limits it would be built with.</summary>
    public sealed class DealSizeRow
    {
        public DealPlan Plan;
        public PlacementLimits Limits;
        public List<string> ImpossibleLimits;

        public string Status => !Plan.IsValid ? "Rejected: " + Plan.RejectionReason
            : ImpossibleLimits.Count > 0 ? "Rejected: " + string.Join("; ", ImpossibleLimits)
            : "OK";
    }

    /// <summary>The deal-size table for every mode and bet, for the Editor window and the CSV export.</summary>
    public static class DealSizeTable
    {
        public static IReadOnlyList<DealSizeRow> Build(PoolConfig config)
        {
            return new DealSizeCalculator(config).PlanAll().Select(plan => ToRow(plan, config)).ToList();
        }

        public static DealSizeRow ToRow(DealPlan plan, PoolConfig config)
        {
            var row = new DealSizeRow { Plan = plan, ImpossibleLimits = new List<string>() };
            if (!plan.IsValid) return row;
            row.Limits = PlacementLimits.For(plan, config.Placement);
            row.ImpossibleLimits = PlacementLimits.FindImpossibleLimits(plan, row.Limits);
            return row;
        }

        public static string ToCsv(IEnumerable<DealSizeRow> rows, PoolConfig config)
        {
            var csv = new Csv().Row("Mode", "Bet", "Payout per paired cycle", "Paired cycles", "Paid spins",
                "Regular tickets", "Big tickets", "Prize tickets", "Losing tickets", "Total wagers", "Total payouts",
                "RTP", "Max consecutive losses", "Max Regular gap", "Max Big gap", "Config version", "Status");

            return csv.Rows(rows, r => new object[]
            {
                r.Plan.Mode,
                Money.ToDollars(r.Plan.BetCents),
                Money.ToDollars(r.Plan.PayoutPerPairedCycleCents),
                r.Plan.IsValid ? (object)r.Plan.PairedCycles : "",
                r.Plan.IsValid ? (object)r.Plan.PaidSpins : "",
                r.Plan.IsValid ? (object)r.Plan.RegularTickets : "",
                r.Plan.IsValid ? (object)r.Plan.BigTickets : "",
                r.Plan.IsValid ? (object)r.Plan.PrizeTickets : "",
                r.Plan.IsValid ? (object)r.Plan.LosingTickets : "",
                r.Plan.IsValid ? Money.ToDollars(r.Plan.TotalWagerCents) : "",
                r.Plan.IsValid ? Money.ToDollars(r.Plan.TotalPayoutCents) : "",
                r.Plan.IsValid ? r.Plan.PlannedRtp.Format() : "",
                r.Limits?.MaxConsecutiveLosses.ToString() ?? "",
                r.Limits?.MaxRegularGap.ToString() ?? "",
                r.Limits?.MaxBigGap.ToString() ?? "",
                config.ConfigVersion,
                r.Status
            }).ToString();
        }
    }
}
