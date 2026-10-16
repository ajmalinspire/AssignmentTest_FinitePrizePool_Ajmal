using System.Collections.Generic;

namespace FinitePrizePool.Engine.Config
{
    // Plain data classes that mirror the config JSON one to one. They hold no behaviour; the validated,
    // read-only view the engine works with is PoolConfig.

    public sealed class PoolConfigData
    {
        public string ConfigVersion;
        public string Note;
        public int TargetRtpBasisPoints = 9000;
        public RtpMode RtpMode = RtpMode.Exact;
        public int ToleranceBasisPoints = 10;
        public long BonusUnitCents = 100;
        public List<long> BetLevelsCents = new List<long>();
        public int MaxGenerationAttempts = 1000;
        public PlacementSettings Placement = new PlacementSettings();
        public List<TestPlayerData> TestPlayers = new List<TestPlayerData>();
        public List<ModeData> Modes = new List<ModeData>();
    }

    public enum RtpMode
    {
        /// <summary>A deal pays exactly the target RTP (default, §2).</summary>
        Exact,

        /// <summary>Stretch goal (§6): the planned RTP may sit within ± ToleranceBasisPoints of the target.</summary>
        Tolerance
    }

    public sealed class ModeData
    {
        public string Name;
        public List<BagEntryData> RegularBag = new List<BagEntryData>();
        public List<BagEntryData> BigBag = new List<BagEntryData>();
    }

    /// <summary>One line of a bag: either a multiplier or a fixed bonus, repeated Count times.</summary>
    public sealed class BagEntryData
    {
        public decimal? Multiplier;
        public long? BonusUnits;
        public int Count = 1;
    }

    public sealed class PlacementSettings
    {
        /// <summary>The "2 ×" in every §4.4 limit.</summary>
        public int LimitFactor = 2;

        /// <summary>Optional fixed values, so tighter limits can be tested (§4.4). Null = use the formula.</summary>
        public int? MaxConsecutiveLossesOverride;
        public int? MaxRegularGapOverride;
        public int? MaxBigGapOverride;

        public bool ForbidRepeatingInterval = true;

        /// <summary>"ConstrainedShuffle" (default) or "ShuffleAndCheck" (the literal §4.3 step 2).</summary>
        public string Strategy = PlacementStrategyNames.ConstrainedShuffle;
    }

    public static class PlacementStrategyNames
    {
        public const string ConstrainedShuffle = "ConstrainedShuffle";
        public const string ShuffleAndCheck = "ShuffleAndCheck";
    }

    public sealed class TestPlayerData
    {
        public string Id;
        public string Name;
        public long StartingBalanceCents;
    }
}
