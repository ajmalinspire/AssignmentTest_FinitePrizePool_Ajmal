using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;

namespace FinitePrizePool.Engine.Maths
{
    /// <summary>
    /// The size of one deal for a mode and bet (§4.2): how many paired cycles, paid spins, prize and losing
    /// tickets it holds, and what it wagers and pays in total. A rejected plan carries the reason instead.
    /// </summary>
    public sealed class DealPlan
    {
        public string Mode { get; private set; }
        public long BetCents { get; private set; }
        public string ConfigVersion { get; private set; }
        public int TargetRtpBasisPoints { get; private set; }
        public RtpMode RtpMode { get; private set; }

        public bool IsValid { get; private set; }
        public string RejectionReason { get; private set; }

        public long PayoutPerPairedCycleCents { get; private set; }
        public long PairedCycles { get; private set; }
        public long RegularTicketsPerCycle { get; private set; }
        public long BigTicketsPerCycle { get; private set; }
        public long PaidSpins { get; private set; }

        public long RegularTickets => PairedCycles * RegularTicketsPerCycle;
        public long BigTickets => PairedCycles * BigTicketsPerCycle;
        public long PrizeTickets => RegularTickets + BigTickets;
        public long LosingTickets => PaidSpins - PrizeTickets;
        public long TotalWagerCents => PaidSpins * BetCents;
        public long TotalPayoutCents => PairedCycles * PayoutPerPairedCycleCents;

        /// <summary>The RTP this plan pays when fully used. Equal to the target in Exact mode.</summary>
        public Rtp PlannedRtp => new Rtp(TotalPayoutCents, TotalWagerCents);

        private DealPlan() { }

        internal static DealPlan Valid(PoolConfig config, ModeDefinition mode, long betCents, long payoutPerCycle, long cycles, long paidSpins)
        {
            var plan = Base(config, mode.Name, betCents);
            plan.IsValid = true;
            plan.PayoutPerPairedCycleCents = payoutPerCycle;
            plan.PairedCycles = cycles;
            plan.RegularTicketsPerCycle = mode.Regular.TicketCount;
            plan.BigTicketsPerCycle = mode.Big.TicketCount;
            plan.PaidSpins = paidSpins;
            return plan;
        }

        internal static DealPlan Rejected(PoolConfig config, string mode, long betCents, string reason, long payoutPerCycle = 0, long cycles = 0)
        {
            var plan = Base(config, mode, betCents);
            plan.IsValid = false;
            plan.RejectionReason = reason;
            plan.PayoutPerPairedCycleCents = payoutPerCycle;
            plan.PairedCycles = cycles;
            return plan;
        }

        private static DealPlan Base(PoolConfig config, string mode, long betCents)
        {
            return new DealPlan
            {
                Mode = mode,
                BetCents = betCents,
                ConfigVersion = config.ConfigVersion,
                TargetRtpBasisPoints = config.TargetRtpBasisPoints,
                RtpMode = config.RtpMode
            };
        }

        public override string ToString()
        {
            if (!IsValid) return Mode + " " + Money.Format(BetCents) + ": rejected (" + RejectionReason + ")";
            return Mode + " " + Money.Format(BetCents) + ": " + PairedCycles + " paired cycle(s), " + PaidSpins + " spins, "
                   + PrizeTickets + " prizes, " + LosingTickets + " losses, RTP " + PlannedRtp.Format();
        }
    }
}
