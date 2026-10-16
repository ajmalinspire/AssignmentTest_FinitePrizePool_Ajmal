using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Reports
{
    /// <summary>Stretch goal (§6): CSV reports of completed and retired deals, and of the retire log.</summary>
    public static class AuditCsv
    {
        public static string FinishedDeals(IEnumerable<FinishedDealRecord> records)
        {
            return new Csv()
                .Row("Deal ID", "Mode", "Bet", "Config version", "State", "Tickets", "Tickets used", "Tickets remaining",
                    "Total wagers", "Total payouts", "Target RTP", "Planned RTP", "Actual RTP", "Reached target",
                    "Created (UTC)", "Activated (UTC)", "Finished (UTC)", "Note")
                .Rows(records.OrderBy(r => r.FinishedUtc), r => new object[]
                {
                    r.DealId, r.Mode, Money.ToDollars(r.BetCents), r.ConfigVersion, r.State, r.TicketCount, r.TicketsUsed,
                    r.TicketsRemaining, Money.ToDollars(r.TotalWagerCents), Money.ToDollars(r.TotalPayoutCents),
                    Rtp.FormatBasisPoints(r.TargetRtpBasisPoints), Percent(r.PlannedRtpPercent), Percent(r.ActualRtpPercent),
                    r.ReachedTarget ? "yes" : "no", Time(r.CreatedUtc), r.ActivatedUtc.HasValue ? Time(r.ActivatedUtc.Value) : "",
                    Time(r.FinishedUtc), r.Note
                })
                .ToString();
        }

        public static string RetireLog(IEnumerable<RetireRecord> records)
        {
            return new Csv()
                .Row("Retired deal", "Mode", "Bet", "Reason", "Time (UTC)", "Old config version", "New config version",
                    "Replacement deal", "Tickets used", "Tickets remaining", "Wagers", "Payouts", "Actual RTP", "Reached target")
                .Rows(records.OrderBy(r => r.TimeUtc), r => new object[]
                {
                    r.RetiredDealId, r.Mode, Money.ToDollars(r.BetCents), r.Reason, Time(r.TimeUtc), r.OldConfigVersion,
                    r.NewConfigVersion, r.ReplacementDealId, r.TicketsUsed, r.TicketsRemaining, Money.ToDollars(r.WagerCents),
                    Money.ToDollars(r.PayoutCents), Percent(r.ActualRtpPercent), r.ReachedTarget ? "yes" : "no"
                })
                .ToString();
        }

        private static string Percent(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture) + "%";
        private static string Time(System.DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
