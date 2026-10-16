using System;
using System.Collections.Generic;
using System.ComponentModel;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Placement;
using Newtonsoft.Json;

namespace FinitePrizePool.Engine.Building
{
    /// <summary>
    /// One ticket of a deal, in play order. Loss tickets pay nothing and belong to no cycle. Fields at their
    /// default value are left out of the JSON (a loss is just position, category and kind), which halves the
    /// size of the large deal files; reading fills the defaults back in.
    /// </summary>
    public sealed class Ticket
    {
        public int Position;
        public SlotCategory Category;

        /// <summary>The Regular or Big cycle this ticket came from (0-based); -1 for a loss.</summary>
        [DefaultValue(-1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int Cycle = -1;

        public PrizeKind Kind;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public decimal Multiplier;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long BonusUnits;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long AwardCents;

        public PrizeDefinition Prize => new PrizeDefinition(Kind, Multiplier, BonusUnits);
    }

    /// <summary>
    /// A complete, ordered deal as it is saved to disk before first use (§4.3 step 4). It is never changed
    /// after that: play progress lives in the ledger, so the ticket list stays exactly as it was verified,
    /// and unused tickets of a retired deal stay on disk for audit.
    ///
    /// It carries a snapshot of its own bags and rules, so it can be verified without the current config.
    /// </summary>
    public sealed class DealDocument
    {
        public int SchemaVersion = 1;
        public string DealId;
        public string Mode;
        public long BetCents;
        public string ConfigVersion;
        public int TargetRtpBasisPoints;
        public RtpMode RtpMode;
        public int ToleranceBasisPoints;
        public long BonusUnitCents;

        public long PairedCycles;
        public long PaidSpins;
        public long RegularTickets;
        public long BigTickets;
        public long LosingTickets;
        public long PlannedWagerCents;
        public long PlannedPayoutCents;

        public PlacementLimits Limits;
        public string PlacementStrategy;
        public int GenerationAttempts;
        public DateTime CreatedUtc;

        public List<BagEntryData> RegularBag = new List<BagEntryData>();
        public List<BagEntryData> BigBag = new List<BagEntryData>();
        public List<Ticket> Tickets = new List<Ticket>();
    }
}
