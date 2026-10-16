using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;
using FinitePrizePool.Engine.Placement;

namespace FinitePrizePool.Engine.Reports
{
    public sealed class PlacementTrial
    {
        public string Strategy;
        public int Attempts;
        public int Successes;
        public decimal SuccessPercent => Attempts == 0 ? 0 : Successes * 100m / Attempts;
    }

    /// <summary>
    /// How often one attempt of a placement strategy meets the §4.4 limits for a deal. Used for the design note
    /// to show why the literal "shuffle and check" cannot build the larger deals within 1,000 attempts.
    /// </summary>
    public static class PlacementSuccessRates
    {
        public static PlacementTrial Measure(PoolConfig config, DealPlan plan, IPlacementStrategy strategy, int attempts, ISecureRandom random = null)
        {
            random = random ?? new CryptoSecureRandom();
            var limits = PlacementLimits.For(plan, config.Placement);
            var trial = new PlacementTrial { Strategy = strategy.Name, Attempts = attempts };

            for (var i = 0; i < attempts; i++)
            {
                var slots = strategy.TryGenerate(plan, limits, random);
                if (slots == null) continue;
                if (PlacementValidator.Validate(slots, limits, plan.RegularTickets, plan.BigTickets, plan.LosingTickets).IsValid)
                    trial.Successes++;
            }
            return trial;
        }
    }
}
