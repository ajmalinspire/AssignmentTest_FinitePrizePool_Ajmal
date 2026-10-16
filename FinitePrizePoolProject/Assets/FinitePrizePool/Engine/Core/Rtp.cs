using System.Globalization;

namespace FinitePrizePool.Engine.Core
{
    /// <summary>
    /// Return-To-Player as an exact fraction (payouts / wagers). It is only turned into a decimal for display,
    /// and "exactly the target" is decided with integer arithmetic.
    /// </summary>
    public readonly struct Rtp
    {
        public const long BasisPointsPerWhole = 10000;

        public long PayoutCents { get; }
        public long WagerCents { get; }

        public Rtp(long payoutCents, long wagerCents)
        {
            PayoutCents = payoutCents;
            WagerCents = wagerCents;
        }

        public bool HasWagers => WagerCents > 0;

        /// <summary>True only when payouts / wagers equals targetBasisPoints / 10000 exactly.</summary>
        public bool IsExactly(int targetBasisPoints)
        {
            return HasWagers && checked(PayoutCents * BasisPointsPerWhole) == checked(WagerCents * targetBasisPoints);
        }

        /// <summary>RTP in percent with 28-digit decimal precision, for display and tolerance checks.</summary>
        public decimal Percent => HasWagers ? PayoutCents * 100m / WagerCents : 0m;

        public string Format(int decimals = 4)
        {
            if (!HasWagers) return "n/a";
            return Percent.ToString("F" + decimals, CultureInfo.InvariantCulture) + "%";
        }

        public override string ToString() => Format();

        public static string FormatBasisPoints(int basisPoints)
        {
            return (basisPoints / 100m).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }
    }
}
