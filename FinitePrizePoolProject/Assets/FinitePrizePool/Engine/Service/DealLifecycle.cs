using System;
using System.Collections.Generic;
using FinitePrizePool.Engine.Building;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Service
{
    /// <summary>
    /// The deal state machine (§4.6, §4.7): Ready → Active → Completed, plus Retired. Every method expects
    /// the caller to hold the service lock and to save afterwards (history first, then the ledger).
    /// </summary>
    internal sealed class DealLifecycle
    {
        private readonly PoolState _state;
        private readonly DealFactory _factory;
        private readonly IClock _clock;
        private readonly IEngineLog _log;

        public DealLifecycle(PoolState state, DealFactory factory, IClock clock, IEngineLog log)
        {
            _state = state;
            _factory = factory;
            _clock = clock;
            _log = log;
        }

        public void Activate(SlotState slot, DealStatus deal, List<Ticket> tickets)
        {
            var now = _clock.UtcNow;
            deal.State = DealState.Active;
            deal.ActivatedUtc = now;
            deal.VerifiedUtc = now;
            slot.Active = deal;
            _state.HoldTickets(deal.DealId, tickets);
        }

        /// <summary>The final ticket was used: record the deal as Completed and switch to its Ready successor.</summary>
        public void Complete(SlotState slot)
        {
            var deal = slot.Active;
            deal.State = DealState.Completed;
            var record = Record(deal, DealState.Completed, null);
            _state.History.Put(record);
            _state.ReleaseTickets(deal.DealId);
            slot.Active = null;
            _log.Info("Deal " + deal.DealId + " completed: " + record.TicketCount + " tickets, RTP " + record.ActualRtpPercent.ToString("0.####") + "%.");
            PromoteSuccessor(slot);
        }

        /// <summary>Makes the Ready successor the active deal, after verifying it again from the store.</summary>
        public bool PromoteSuccessor(SlotState slot)
        {
            var ready = slot.Ready;
            slot.Ready = null;
            if (ready == null)
            {
                _log.Warning("No Ready successor for " + slot.Mode + " " + Money.Format(slot.BetCents) + "; play there waits for a build.");
                return false;
            }

            if (ready.ConfigVersion != _state.Config.ConfigVersion)
            {
                Discard(ready, "built on config " + ready.ConfigVersion + ", the config is now " + _state.Config.ConfigVersion);
                return false;
            }

            var check = _factory.LoadVerified(ready.DealId);
            if (!check.Succeeded)
            {
                Discard(ready, check.FailureReason);
                return false;
            }

            Activate(slot, ready, check.Tickets);
            return true;
        }

        /// <summary>A Ready deal that will never be played (stale config, failed check) is recorded as Retired, unused.</summary>
        public void Discard(DealStatus deal, string why)
        {
            deal.State = DealState.Retired;
            _state.History.Put(Record(deal, DealState.Retired, "Discarded before use: " + why));
            _log.Warning("Deal " + deal.DealId + " discarded before use: " + why);
        }

        /// <summary>
        /// Retires the active deal (§4.7): stop using it, mark it Retired, switch to a freshly verified
        /// replacement and log the details. The ticket file stays on disk for audit.
        /// </summary>
        public RetireRecord Retire(SlotState slot, string reason, bool requireReplacement)
        {
            var old = slot.Active ?? throw new InvalidOperationException("There is no active deal to retire.");
            var replacement = FindReplacement(slot, out var replacementTickets, out var failure);
            if (replacement == null && requireReplacement)
                throw new InvalidOperationException("Not retired: no verified replacement could be built (" + failure + ").");

            old.State = DealState.Retired;
            var record = new RetireRecord
            {
                RetiredDealId = old.DealId,
                Mode = old.Mode,
                BetCents = old.BetCents,
                Reason = reason,
                TimeUtc = _clock.UtcNow,
                OldConfigVersion = old.ConfigVersion,
                NewConfigVersion = replacement?.ConfigVersion,
                ReplacementDealId = replacement?.DealId,
                TicketsUsed = old.Position,
                TicketsRemaining = old.TicketCount - old.Position,
                WagerCents = old.WagerCents,
                PayoutCents = old.PayoutCents,
                ActualRtpPercent = new Rtp(old.PayoutCents, old.WagerCents).Percent,
                ReachedTarget = false
            };

            _state.History.RetireLog.Add(record);
            _state.History.Put(Record(old, DealState.Retired, "Retired: " + reason));
            _state.ReleaseTickets(old.DealId);
            slot.Active = null;
            if (replacement != null) Activate(slot, replacement, replacementTickets);

            _log.Warning("Deal " + old.DealId + " retired (" + reason + "); replacement " + (replacement?.DealId ?? "none") + ".");
            return record;
        }

        /// <summary>The Ready successor if it is on the current config and still verifies, otherwise a deal built now.</summary>
        private DealStatus FindReplacement(SlotState slot, out List<Ticket> tickets, out string failure)
        {
            tickets = null;
            failure = null;

            var ready = slot.Ready;
            slot.Ready = null;
            if (ready != null && ready.ConfigVersion == _state.Config.ConfigVersion)
            {
                var check = _factory.LoadVerified(ready.DealId);
                if (check.Succeeded)
                {
                    tickets = check.Tickets;
                    return ready;
                }
                Discard(ready, check.FailureReason);
            }
            else if (ready != null)
            {
                Discard(ready, "built on config " + ready.ConfigVersion + ", the config is now " + _state.Config.ConfigVersion);
            }

            var created = _factory.Create(_state.NextDealId(), slot.Mode, slot.BetCents, _state.Config);
            if (!created.Succeeded)
            {
                failure = created.FailureReason;
                return null;
            }
            tickets = created.Tickets;
            return created.Status;
        }

        private FinishedDealRecord Record(DealStatus deal, DealState state, string note)
        {
            var actual = new Rtp(deal.PayoutCents, deal.WagerCents);
            var usedUp = deal.Position == deal.TicketCount;
            var paidPlan = deal.PayoutCents == deal.PlannedPayoutCents && deal.WagerCents == deal.PlannedWagerCents;
            var exactWhereRequired = deal.RtpMode != RtpMode.Exact || actual.IsExactly(deal.TargetRtpBasisPoints);

            return new FinishedDealRecord
            {
                DealId = deal.DealId,
                Mode = deal.Mode,
                BetCents = deal.BetCents,
                ConfigVersion = deal.ConfigVersion,
                State = state,
                TicketCount = deal.TicketCount,
                TicketsUsed = deal.Position,
                TicketsRemaining = deal.TicketCount - deal.Position,
                TotalWagerCents = deal.WagerCents,
                TotalPayoutCents = deal.PayoutCents,
                TargetRtpBasisPoints = deal.TargetRtpBasisPoints,
                PlannedRtpPercent = new Rtp(deal.PlannedPayoutCents, deal.PlannedWagerCents).Percent,
                ActualRtpPercent = actual.Percent,
                ReachedTarget = state == DealState.Completed && usedUp && paidPlan && exactWhereRequired,
                CreatedUtc = deal.CreatedUtc,
                ActivatedUtc = deal.ActivatedUtc,
                FinishedUtc = _clock.UtcNow,
                Note = note
            };
        }
    }
}
