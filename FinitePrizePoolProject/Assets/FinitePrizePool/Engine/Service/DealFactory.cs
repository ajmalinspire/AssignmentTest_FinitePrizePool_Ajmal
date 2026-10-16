using System;
using System.Collections.Generic;
using FinitePrizePool.Engine.Building;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Service
{
    internal sealed class CreatedDeal
    {
        public bool Succeeded;
        public string FailureReason;
        public DealStatus Status;
        public List<Ticket> Tickets;
    }

    /// <summary>
    /// Turns a plan into a playable deal: build it, save the complete ordered deal, read it back from the store
    /// and verify what was saved (§4.3 steps 4 and 5). Safe to run on a background thread: it touches only
    /// its own deal file.
    /// </summary>
    internal sealed class DealFactory
    {
        private readonly IPoolStore _store;
        private readonly DealBuilder _builder;
        private readonly IClock _clock;
        private readonly IEngineLog _log;

        public DealFactory(IPoolStore store, ISecureRandom random, IClock clock, IEngineLog log)
        {
            _store = store;
            _builder = new DealBuilder(random, log, clock);
            _clock = clock;
            _log = log;
        }

        public CreatedDeal Create(string dealId, string mode, long betCents, PoolConfig config)
        {
            var plan = new DealSizeCalculator(config).PlanSmallest(mode, betCents);
            var built = _builder.Build(dealId, plan, config);
            if (!built.Succeeded) return Failed(built.FailureReason);

            _store.SaveDeal(built.Deal);
            var check = LoadVerified(dealId);
            if (!check.Succeeded) return check;

            check.Status = new DealStatus
            {
                DealId = dealId,
                Mode = mode,
                BetCents = betCents,
                ConfigVersion = config.ConfigVersion,
                State = DealState.Ready,
                TicketCount = plan.PaidSpins,
                TargetRtpBasisPoints = config.TargetRtpBasisPoints,
                RtpMode = config.RtpMode,
                PlannedWagerCents = plan.TotalWagerCents,
                PlannedPayoutCents = plan.TotalPayoutCents,
                CreatedUtc = built.Deal.CreatedUtc,
                VerifiedUtc = _clock.UtcNow
            };
            _log.Info("Built " + dealId + ": " + plan + " in " + built.Attempts + " attempt(s).");
            return check;
        }

        /// <summary>Reads a saved deal and verifies it from its tickets alone.</summary>
        public CreatedDeal LoadVerified(string dealId)
        {
            DealDocument saved;
            try { saved = _store.LoadDeal(dealId); }
            catch (Exception e) { return Failed("deal " + dealId + " could not be read: " + e.Message); }

            var verification = DealVerifier.Verify(saved);
            if (!verification.IsValid)
            {
                var reason = "deal " + dealId + " failed verification: " + string.Join("; ", verification.Problems);
                _log.Error(reason);
                return Failed(reason);
            }
            return new CreatedDeal { Succeeded = true, Tickets = saved.Tickets };
        }

        private static CreatedDeal Failed(string reason) => new CreatedDeal { Succeeded = false, FailureReason = reason };
    }
}
