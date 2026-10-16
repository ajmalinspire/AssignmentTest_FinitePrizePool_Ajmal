using System;
using System.Collections.Generic;

namespace FinitePrizePool.Engine.Persistence
{
    /// <summary>Records of finished deals (Completed or Retired) and the retire log, kept for audit.</summary>
    public sealed class DealHistory
    {
        public int SchemaVersion = 1;
        public List<FinishedDealRecord> Finished = new List<FinishedDealRecord>();
        public List<RetireRecord> RetireLog = new List<RetireRecord>();

        /// <summary>Adds or replaces the record for a deal, so repeating a step after a crash cannot duplicate it.</summary>
        public void Put(FinishedDealRecord record)
        {
            Finished.RemoveAll(r => r.DealId == record.DealId);
            Finished.Add(record);
        }
    }

    public sealed class FinishedDealRecord
    {
        public string DealId;
        public string Mode;
        public long BetCents;
        public string ConfigVersion;
        public DealState State;
        public long TicketCount;
        public long TicketsUsed;
        public long TicketsRemaining;
        public long TotalWagerCents;
        public long TotalPayoutCents;
        public int TargetRtpBasisPoints;

        /// <summary>Planned RTP in percent (differs from the target only in tolerance mode).</summary>
        public decimal PlannedRtpPercent;

        /// <summary>Payouts ÷ wagers over the tickets actually played, in percent.</summary>
        public decimal ActualRtpPercent;

        /// <summary>Only a Completed deal that paid exactly its plan can be true. A Retired deal is always false.</summary>
        public bool ReachedTarget;

        public DateTime CreatedUtc;
        public DateTime? ActivatedUtc;
        public DateTime FinishedUtc;
        public string Note;
    }

    /// <summary>One retire action (§4.7), with every field the brief asks for.</summary>
    public sealed class RetireRecord
    {
        public string RetiredDealId;
        public string Mode;
        public long BetCents;
        public string Reason;
        public DateTime TimeUtc;
        public string OldConfigVersion;
        public string NewConfigVersion;
        public string ReplacementDealId;
        public long TicketsUsed;
        public long TicketsRemaining;
        public long WagerCents;
        public long PayoutCents;
        public decimal ActualRtpPercent;

        /// <summary>Always false: a retired deal never reached its target RTP.</summary>
        public bool ReachedTarget;
    }
}
