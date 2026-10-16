using System;
using System.Collections.Generic;
using FinitePrizePool.Engine.Config;

namespace FinitePrizePool.Engine.Persistence
{
    public enum DealState
    {
        Ready,
        Active,
        Completed,
        Retired
    }

    /// <summary>
    /// Everything that changes during play, saved as one file in one atomic write per spin: balances,
    /// each deal's ticket position and totals, and the last spin. One write means a spin is either fully
    /// recorded or not at all, never half.
    /// </summary>
    public sealed class Ledger
    {
        public int SchemaVersion = 1;
        public long NextSpinId = 1;
        public long NextDealNumber = 1;
        public List<PlayerState> Players = new List<PlayerState>();
        public List<SlotState> Slots = new List<SlotState>();

        /// <summary>A spin whose result the player has not seen yet (game closed mid-animation). Null when none.</summary>
        public SpinRecord PendingSpin;

        /// <summary>The most recent spin, kept so the spin log can be repaired after a crash.</summary>
        public SpinRecord LastSpin;
    }

    public sealed class PlayerState
    {
        public string Id;
        public string Name;
        public long BalanceCents;

        /// <summary>How many spins this player has made, across every mode and bet. Saved with the spin, in the same write.</summary>
        public long SpinCount;
    }

    /// <summary>One mode/bet combination. Every player profile on the device shares its deals.</summary>
    public sealed class SlotState
    {
        public string Mode;
        public long BetCents;
        public DealStatus Active;
        public DealStatus Ready;

        /// <summary>Why the last build for this slot was rejected; it is not retried on the same config version.</summary>
        public string BuildError;
        public string BuildErrorConfigVersion;
    }

    /// <summary>The live state of one deal. Its tickets are in the deal file and never change.</summary>
    public sealed class DealStatus
    {
        public string DealId;
        public string Mode;
        public long BetCents;
        public string ConfigVersion;
        public DealState State;
        public long TicketCount;

        /// <summary>Index of the next ticket to play.</summary>
        public long Position;

        public long WagerCents;
        public long PayoutCents;
        public int TargetRtpBasisPoints;
        public RtpMode RtpMode;
        public long PlannedWagerCents;
        public long PlannedPayoutCents;
        public DateTime CreatedUtc;
        public DateTime? ActivatedUtc;
        public DateTime VerifiedUtc;

        public DealStatus Copy() => (DealStatus)MemberwiseClone();
    }

    /// <summary>One spin, saved before any animation plays (§4.5).</summary>
    public sealed class SpinRecord
    {
        /// <summary>Unique on the device: one counter shared by every player, mode and bet.</summary>
        public long SpinId;

        /// <summary>This player's own spin number: 1 for their first spin. 0 in records saved before it existed.</summary>
        public long PlayerSpinNumber;

        public DateTime TimeUtc;
        public string PlayerId;
        public string Mode;
        public long BetCents;
        public string DealId;
        public string ConfigVersion;
        public long TicketPosition;
        public long WagerCents;
        public long AwardCents;
        public PrizeKind PrizeType;
        public decimal Multiplier;
        public long BonusUnits;
        public long BalanceBeforeCents;
        public long BalanceAfterCents;

        /// <summary>True when this spin used the deal's last ticket.</summary>
        public bool CompletedDeal;

        public SpinRecord Copy() => (SpinRecord)MemberwiseClone();
    }
}
