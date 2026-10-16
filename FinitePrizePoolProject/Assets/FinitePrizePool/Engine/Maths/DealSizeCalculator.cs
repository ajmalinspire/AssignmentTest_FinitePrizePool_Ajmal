using System;
using System.Collections.Generic;
using System.Linq;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;

namespace FinitePrizePool.Engine.Maths
{
    /// <summary>
    /// Works out deal sizes (§4.2) with integer cents only.
    ///
    /// For k paired cycles paying P cents each, at bet b cents and target t basis points, the deal needs
    /// N = k·P·10000 / (b·t) paid spins. N must be a whole number: the engine never rounds to make a deal fit.
    /// The smallest k that makes it whole is (b·t) / gcd(P·10000, b·t).
    /// </summary>
    public sealed class DealSizeCalculator
    {
        private readonly PoolConfig _config;

        public DealSizeCalculator(PoolConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>One row per mode and bet level (40 for this test).</summary>
        public IReadOnlyList<DealPlan> PlanAll()
        {
            return _config.ModeNames
                .SelectMany(mode => _config.BetLevelsCents.Select(bet => PlanSmallest(mode, bet)))
                .ToList();
        }

        /// <summary>The smallest deal for a mode and bet, in the config's RTP mode.</summary>
        public DealPlan PlanSmallest(string modeName, long betCents)
        {
            var mode = _config.GetMode(modeName);
            if (!TryPayoutPerPairedCycle(mode, betCents, out var payoutPerCycle, out var reason))
                return DealPlan.Rejected(_config, modeName, betCents, reason);

            try
            {
                return _config.RtpMode == RtpMode.Tolerance
                    ? PlanSmallestWithinTolerance(mode, betCents, payoutPerCycle)
                    : PlanSmallestExact(mode, betCents, payoutPerCycle);
            }
            catch (OverflowException)
            {
                return DealPlan.Rejected(_config, modeName, betCents, "deal is too large to count in 64-bit cents", payoutPerCycle);
            }
        }

        /// <summary>A deal with a fixed number of paired cycles. Rejected if the paid spins would not be whole.</summary>
        public DealPlan PlanWithCycles(string modeName, long betCents, long pairedCycles)
        {
            var mode = _config.GetMode(modeName);
            if (pairedCycles < 1) return DealPlan.Rejected(_config, modeName, betCents, "paired cycles must be at least 1");
            if (!TryPayoutPerPairedCycle(mode, betCents, out var payoutPerCycle, out var reason))
                return DealPlan.Rejected(_config, modeName, betCents, reason);

            try
            {
                var numerator = checked(pairedCycles * payoutPerCycle * Rtp.BasisPointsPerWhole);
                var denominator = checked(betCents * _config.TargetRtpBasisPoints);
                if (numerator % denominator != 0)
                {
                    var exact = (decimal)numerator / denominator;
                    return DealPlan.Rejected(_config, modeName, betCents,
                        "needs " + exact.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                        + " paid spins with " + pairedCycles + " paired cycle(s): not a whole number of tickets",
                        payoutPerCycle, pairedCycles);
                }
                return Finish(mode, betCents, payoutPerCycle, pairedCycles, numerator / denominator);
            }
            catch (OverflowException)
            {
                return DealPlan.Rejected(_config, modeName, betCents, "deal is too large to count in 64-bit cents", payoutPerCycle, pairedCycles);
            }
        }

        /// <summary>Payout of one paired cycle: multiplier total × bet + bonus units × unit value.</summary>
        public bool TryPayoutPerPairedCycle(ModeDefinition mode, long betCents, out long payoutCents, out string reason)
        {
            payoutCents = 0;
            reason = null;
            foreach (var prize in mode.Regular.Tickets.Concat(mode.Big.Tickets))
            {
                if (!prize.TryGetAwardCents(betCents, _config.BonusUnitCents, out var award))
                {
                    reason = prize.Describe() + " at " + Money.Format(betCents) + " is not a whole number of cents";
                    return false;
                }
                payoutCents = checked(payoutCents + award);
            }
            return true;
        }

        private DealPlan PlanSmallestExact(ModeDefinition mode, long betCents, long payoutPerCycle)
        {
            var numeratorPerCycle = checked(payoutPerCycle * Rtp.BasisPointsPerWhole);
            var denominator = checked(betCents * _config.TargetRtpBasisPoints);
            var gcd = IntMath.Gcd(numeratorPerCycle, denominator);
            var cycles = denominator / gcd;
            var paidSpins = numeratorPerCycle / gcd;
            return Finish(mode, betCents, payoutPerCycle, cycles, paidSpins);
        }

        /// <summary>
        /// Stretch goal (§6): the smallest deal whose RTP lands within ± tolerance of the target, using one
        /// paired cycle where possible. The spin count is the whole number closest to the exact target that
        /// stays inside the band; the planned RTP is recorded on the plan.
        /// </summary>
        private DealPlan PlanSmallestWithinTolerance(ModeDefinition mode, long betCents, long payoutPerCycle)
        {
            var exactPlan = PlanSmallestExact(mode, betCents, payoutPerCycle);
            var target = _config.TargetRtpBasisPoints;
            var tolerance = _config.ToleranceBasisPoints;
            var lastCycles = exactPlan.IsValid ? exactPlan.PairedCycles : exactPlan.PairedCycles + 1;

            for (long cycles = 1; cycles < lastCycles; cycles++)
            {
                var numerator = checked(cycles * payoutPerCycle * Rtp.BasisPointsPerWhole);
                var minSpins = IntMath.CeilDiv(numerator, checked(betCents * (target + tolerance)));
                var maxSpins = IntMath.FloorDiv(numerator, checked(betCents * (target - tolerance)));
                if (minSpins > maxSpins) continue;

                var exactDenominator = checked(betCents * target);
                var below = Math.Max(minSpins, Math.Min(maxSpins, numerator / exactDenominator));
                var above = Math.Max(minSpins, Math.Min(maxSpins, below + 1));
                var spins = Math.Abs(numerator - below * exactDenominator) <= Math.Abs(numerator - above * exactDenominator) ? below : above;

                var plan = Finish(mode, betCents, payoutPerCycle, cycles, spins);
                if (plan.IsValid) return plan;
            }
            return exactPlan;
        }

        private DealPlan Finish(ModeDefinition mode, long betCents, long payoutPerCycle, long cycles, long paidSpins)
        {
            var prizeTickets = checked(cycles * mode.TicketsPerPairedCycle);
            if (paidSpins < prizeTickets)
            {
                return DealPlan.Rejected(_config, mode.Name, betCents,
                    paidSpins + " paid spins are fewer than " + prizeTickets + " prize tickets", payoutPerCycle, cycles);
            }
            return DealPlan.Valid(_config, mode, betCents, payoutPerCycle, cycles, paidSpins);
        }
    }
}
