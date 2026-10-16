using System.Collections.Generic;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;
using FinitePrizePool.Engine.Placement;

namespace FinitePrizePool.Engine.Building
{
    public sealed class BuildResult
    {
        public bool Succeeded;
        public DealDocument Deal;
        public string FailureReason;
        public int Attempts;
    }

    /// <summary>
    /// Builds one deal from a plan, following §4.3:
    /// 1. shuffle each Regular cycle on its own and append the cycles in order; the same for Big;
    /// 2. build a category list with exactly R Regular, B Big and L Loss labels and check it against §4.4;
    /// 3. merge: each Regular slot takes the next Regular ticket, each Big slot the next Big ticket.
    /// Saving and verifying (steps 4 and 5) are done by <see cref="DealFactory"/>.
    /// </summary>
    public sealed class DealBuilder
    {
        private readonly ISecureRandom _random;
        private readonly IEngineLog _log;
        private readonly IClock _clock;

        public DealBuilder(ISecureRandom random, IEngineLog log, IClock clock)
        {
            _random = random;
            _log = log;
            _clock = clock;
        }

        public BuildResult Build(string dealId, DealPlan plan, PoolConfig config)
        {
            if (!plan.IsValid) return Fail(dealId, plan, "plan rejected: " + plan.RejectionReason, 0);
            if (plan.PaidSpins > int.MaxValue) return Fail(dealId, plan, "deal has too many tickets to hold in memory", 0);

            var limits = PlacementLimits.For(plan, config.Placement);
            var impossible = PlacementLimits.FindImpossibleLimits(plan, limits);
            if (impossible.Count > 0) return Fail(dealId, plan, "placement limits are impossible: " + string.Join("; ", impossible), 0);

            var strategy = PlacementStrategies.Create(config.Placement.Strategy);
            for (var attempt = 1; attempt <= config.MaxGenerationAttempts; attempt++)
            {
                var slots = strategy.TryGenerate(plan, limits, _random);
                if (slots == null) continue;

                var report = PlacementValidator.Validate(slots, limits, plan.RegularTickets, plan.BigTickets, plan.LosingTickets);
                if (!report.IsValid) continue;

                var deal = Assemble(dealId, plan, config, limits, strategy.Name, attempt, slots);
                return new BuildResult { Succeeded = true, Deal = deal, Attempts = attempt };
            }

            return Fail(dealId, plan, "no category list met the placement limits in " + config.MaxGenerationAttempts
                                      + " attempts (" + limits + ", strategy " + strategy.Name + ")", config.MaxGenerationAttempts);
        }

        private DealDocument Assemble(string dealId, DealPlan plan, PoolConfig config, PlacementLimits limits,
            string strategyName, int attempts, SlotCategory[] slots)
        {
            var mode = config.GetMode(plan.Mode);
            var regularStream = ShuffledCycles(mode.Regular, plan.PairedCycles);
            var bigStream = ShuffledCycles(mode.Big, plan.PairedCycles);

            var deal = new DealDocument
            {
                DealId = dealId,
                Mode = plan.Mode,
                BetCents = plan.BetCents,
                ConfigVersion = config.ConfigVersion,
                TargetRtpBasisPoints = config.TargetRtpBasisPoints,
                RtpMode = config.RtpMode,
                ToleranceBasisPoints = config.ToleranceBasisPoints,
                BonusUnitCents = config.BonusUnitCents,
                PairedCycles = plan.PairedCycles,
                PaidSpins = plan.PaidSpins,
                RegularTickets = plan.RegularTickets,
                BigTickets = plan.BigTickets,
                LosingTickets = plan.LosingTickets,
                PlannedWagerCents = plan.TotalWagerCents,
                PlannedPayoutCents = plan.TotalPayoutCents,
                Limits = limits,
                PlacementStrategy = strategyName,
                GenerationAttempts = attempts,
                CreatedUtc = _clock.UtcNow,
                RegularBag = mode.Regular.ToEntries(),
                BigBag = mode.Big.ToEntries(),
                Tickets = new List<Ticket>(slots.Length)
            };

            int nextRegular = 0, nextBig = 0;
            for (var position = 0; position < slots.Length; position++)
            {
                var ticket = new Ticket { Position = position, Category = slots[position], Kind = PrizeKind.Loss };
                if (slots[position] != SlotCategory.Loss)
                {
                    var source = slots[position] == SlotCategory.Regular ? regularStream[nextRegular++] : bigStream[nextBig++];
                    source.Prize.TryGetAwardCents(plan.BetCents, config.BonusUnitCents, out var award);
                    ticket.Cycle = source.Cycle;
                    ticket.Kind = source.Prize.Kind;
                    ticket.Multiplier = source.Prize.Multiplier;
                    ticket.BonusUnits = source.Prize.BonusUnits;
                    ticket.AwardCents = award;
                }
                deal.Tickets.Add(ticket);
            }
            return deal;
        }

        /// <summary>Each cycle is one full copy of the bag, shuffled on its own; cycles are appended in order.</summary>
        private List<CycleTicket> ShuffledCycles(Bag bag, long cycles)
        {
            var stream = new List<CycleTicket>((int)(cycles * bag.TicketCount));
            var cycleTickets = new List<PrizeDefinition>(bag.TicketCount);
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                cycleTickets.Clear();
                cycleTickets.AddRange(bag.Tickets);
                FisherYates.Shuffle(cycleTickets, _random);
                foreach (var prize in cycleTickets) stream.Add(new CycleTicket(prize, cycle));
            }
            return stream;
        }

        private BuildResult Fail(string dealId, DealPlan plan, string reason, int attempts)
        {
            _log.Error("Deal " + dealId + " (" + plan.Mode + " " + Money.Format(plan.BetCents) + ", config "
                       + plan.ConfigVersion + ") rejected: " + reason);
            return new BuildResult { Succeeded = false, FailureReason = reason, Attempts = attempts };
        }

        private readonly struct CycleTicket
        {
            public readonly PrizeDefinition Prize;
            public readonly int Cycle;

            public CycleTicket(PrizeDefinition prize, int cycle)
            {
                Prize = prize;
                Cycle = cycle;
            }
        }
    }
}
