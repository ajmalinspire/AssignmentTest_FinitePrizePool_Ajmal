using System;
using System.Collections.Generic;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Service
{
    /// <summary>
    /// Keeps every played mode/bet supplied with an active deal and a verified Ready successor (§4.6). Builds
    /// run through the background runner, so play never waits for one; the result is registered under the
    /// service lock. A build that is rejected is recorded on the slot and not retried until the config
    /// changes or an admin asks, so a bad config can never loop forever.
    /// </summary>
    internal sealed class SuccessorScheduler
    {
        private readonly object _sync;
        private readonly PoolState _state;
        private readonly DealFactory _factory;
        private readonly DealLifecycle _lifecycle;
        private readonly IBackgroundRunner _runner;
        private readonly Func<bool> _isClosed;
        private readonly HashSet<string> _building = new HashSet<string>();

        public SuccessorScheduler(object sync, PoolState state, DealFactory factory, DealLifecycle lifecycle,
            IBackgroundRunner runner, Func<bool> isClosed)
        {
            _sync = sync;
            _state = state;
            _factory = factory;
            _lifecycle = lifecycle;
            _runner = runner;
            _isClosed = isClosed;
        }

        public bool IsBuilding(SlotState slot) => _building.Contains(Key(slot.Mode, slot.BetCents));

        /// <summary>Call under the lock. Starts a build if the slot lacks an active deal or a Ready successor.</summary>
        public void EnsureDeals(SlotState slot)
        {
            if (slot.Active != null && slot.Ready != null) return;
            if (IsBuilding(slot)) return;
            if (slot.BuildError != null && slot.BuildErrorConfigVersion == _state.Config.ConfigVersion) return;

            _building.Add(Key(slot.Mode, slot.BetCents));
            var dealId = _state.NextDealId();
            var config = _state.Config;
            var mode = slot.Mode;
            var bet = slot.BetCents;
            _runner.Run(() => BuildAndRegister(dealId, mode, bet, config));
        }

        private void BuildAndRegister(string dealId, string mode, long betCents, PoolConfig config)
        {
            CreatedDeal created;
            try { created = _factory.Create(dealId, mode, betCents, config); }
            catch (Exception e) { created = new CreatedDeal { Succeeded = false, FailureReason = e.Message }; }

            lock (_sync)
            {
                // The game was closed during the build: a later session owns the saves now, so write nothing.
                // The finished deal file stays on disk unused; it was never registered.
                if (_isClosed()) return;

                _building.Remove(Key(mode, betCents));
                var slot = _state.GetOrAddSlot(mode, betCents);

                if (!created.Succeeded)
                {
                    slot.BuildError = created.FailureReason;
                    slot.BuildErrorConfigVersion = config.ConfigVersion;
                    _state.SaveLedger();
                    return;
                }

                Register(slot, created, config);
                _state.SaveHistory();
                _state.SaveLedger();
                EnsureDeals(slot);
            }
        }

        private void Register(SlotState slot, CreatedDeal created, PoolConfig builtWith)
        {
            if (builtWith.ConfigVersion != _state.Config.ConfigVersion)
            {
                _lifecycle.Discard(created.Status, "built on config " + builtWith.ConfigVersion + ", the config changed to "
                                                    + _state.Config.ConfigVersion + " during the build");
                return;
            }

            slot.BuildError = null;
            slot.BuildErrorConfigVersion = null;
            if (slot.Active == null) _lifecycle.Activate(slot, created.Status, created.Tickets);
            else if (slot.Ready == null) slot.Ready = created.Status;
            else _lifecycle.Discard(created.Status, "the slot already had a Ready successor");
        }

        private static string Key(string mode, long betCents) => mode + "|" + betCents;
    }
}
