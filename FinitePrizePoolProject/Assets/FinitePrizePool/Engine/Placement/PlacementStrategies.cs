using System;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;

namespace FinitePrizePool.Engine.Placement
{
    /// <summary>
    /// Produces one candidate category list for a deal: exactly R Regular, B Big and L Loss labels in a random
    /// order. Returning null means "this attempt failed"; the builder then tries again, up to its limit.
    /// </summary>
    public interface IPlacementStrategy
    {
        string Name { get; }
        SlotCategory[] TryGenerate(DealPlan plan, PlacementLimits limits, ISecureRandom random);
    }

    public static class PlacementStrategies
    {
        public static IPlacementStrategy Create(string name)
        {
            switch (name)
            {
                case PlacementStrategyNames.ConstrainedShuffle: return new ConstrainedShufflePlacement();
                case PlacementStrategyNames.ShuffleAndCheck: return new ShuffleAndCheckPlacement();
                default: throw new ArgumentException("Unknown placement strategy '" + name + "'.");
            }
        }
    }

    /// <summary>
    /// The literal §4.3 step 2: build the label list, Fisher–Yates shuffle it, and let the builder check it.
    /// Fine for small deals; for deals with many prizes almost every shuffle breaks a limit somewhere
    /// (see the design note), which is why it is not the default.
    /// </summary>
    public sealed class ShuffleAndCheckPlacement : IPlacementStrategy
    {
        public string Name => PlacementStrategyNames.ShuffleAndCheck;

        public SlotCategory[] TryGenerate(DealPlan plan, PlacementLimits limits, ISecureRandom random)
        {
            var slots = new SlotCategory[plan.PaidSpins];
            var i = 0;
            for (long r = 0; r < plan.RegularTickets; r++) slots[i++] = SlotCategory.Regular;
            for (long b = 0; b < plan.BigTickets; b++) slots[i++] = SlotCategory.Big;
            for (; i < slots.Length; i++) slots[i] = SlotCategory.Loss;
            FisherYates.Shuffle(slots, random);
            return slots;
        }
    }
}
