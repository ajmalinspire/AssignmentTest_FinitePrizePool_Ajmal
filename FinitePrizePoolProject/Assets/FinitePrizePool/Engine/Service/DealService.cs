using System;
using System.Collections.Generic;
using System.Linq;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Service
{
    /// <summary>
    /// The deal engine's front door. One lock guards all state, so spins, background builds and admin actions
    /// never interleave. Save order is always: deal files before use, history before the ledger, the ledger
    /// before the spin log; so a crash at any point reopens into a consistent state.
    /// </summary>
    public sealed class DealService : IDealService, IDealAdmin, IDisposable
    {
        private readonly object _sync = new object();
        private bool _closed;
        private readonly IPoolStore _store;
        private readonly IClock _clock;
        private readonly IEngineLog _log;
        private readonly PoolState _state;
        private readonly DealFactory _factory;
        private readonly DealLifecycle _lifecycle;
        private readonly SuccessorScheduler _scheduler;
        private readonly SavedStateLoader _loader;

        public DealService(PoolConfig config, IPoolStore store, IBackgroundRunner runner,
            ISecureRandom random = null, IClock clock = null, IEngineLog log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? new SystemClock();
            _log = log ?? new NullEngineLog();

            lock (_sync)
            {
                _state = new PoolState(store, config ?? throw new ArgumentNullException(nameof(config)));
                _factory = new DealFactory(store, random ?? new CryptoSecureRandom(), _clock, _log);
                _lifecycle = new DealLifecycle(_state, _factory, _clock, _log);
                _scheduler = new SuccessorScheduler(_sync, _state, _factory, _lifecycle,
                    runner ?? throw new ArgumentNullException(nameof(runner)), () => _closed);
                _loader = new SavedStateLoader(_state, _factory, _lifecycle, store, _log);
                _loader.Open();
                EnsureEveryActiveSlotHasDeals();
            }
        }

        /// <summary>
        /// Stops the service when the game closes. Builds still running in the background then finish without
        /// writing anything, so they cannot overwrite the saves of the next session.
        /// </summary>
        public void Dispose()
        {
            lock (_sync) _closed = true;
        }

        public bool IsClosed
        {
            get { lock (_sync) return _closed; }
        }

        private void ThrowIfClosed()
        {
            if (_closed) throw new ObjectDisposedException(nameof(DealService), "The deal service has been closed.");
        }

        public IReadOnlyList<string> Modes => _state.Config.ModeNames;
        public IReadOnlyList<long> BetLevelsCents => _state.Config.BetLevelsCents;
        public string ConfigVersion => _state.Config.ConfigVersion;
        public PoolConfig Config => _state.Config;

        // ---- game -------------------------------------------------------------------------------------------

        public IReadOnlyList<PlayerView> GetPlayers()
        {
            lock (_sync) return _state.Ledger.Players.Select(ToView).ToList();
        }

        public PlayerView GetPlayer(string playerId)
        {
            lock (_sync)
            {
                var player = _state.FindPlayer(playerId);
                return player == null ? null : ToView(player);
            }
        }

        public void PrepareSlot(string mode, long betCents)
        {
            lock (_sync)
            {
                if (_closed || !_state.Config.HasMode(mode) || !_state.Config.HasBetLevel(betCents)) return;
                _scheduler.EnsureDeals(_state.GetOrAddSlot(mode, betCents));
            }
        }

        public SlotView GetSlot(string mode, long betCents)
        {
            lock (_sync)
            {
                var slot = _state.FindSlot(mode, betCents);
                return slot == null ? new SlotView { Mode = mode, BetCents = betCents } : ToView(slot);
            }
        }

        public SpinOutcome Spin(string playerId, string mode, long betCents)
        {
            lock (_sync)
            {
                ThrowIfClosed();
                var rejection = CheckSpin(playerId, mode, betCents, out var player, out var slot);
                if (rejection != null) return rejection;

                var deal = slot.Active;
                var ticket = _state.TicketsOf(deal)[(int)deal.Position];
                var record = new SpinRecord
                {
                    SpinId = _state.Ledger.NextSpinId++,
                    TimeUtc = _clock.UtcNow,
                    PlayerId = player.Id,
                    Mode = mode,
                    BetCents = betCents,
                    DealId = deal.DealId,
                    ConfigVersion = deal.ConfigVersion,
                    TicketPosition = deal.Position,
                    WagerCents = betCents,
                    AwardCents = ticket.AwardCents,
                    PrizeType = ticket.Kind,
                    Multiplier = ticket.Multiplier,
                    BonusUnits = ticket.BonusUnits,
                    BalanceBeforeCents = player.BalanceCents
                };

                player.BalanceCents = checked(player.BalanceCents - betCents + ticket.AwardCents);
                player.SpinCount++;
                record.PlayerSpinNumber = player.SpinCount;
                deal.Position++;
                deal.WagerCents += betCents;
                deal.PayoutCents += ticket.AwardCents;
                record.BalanceAfterCents = player.BalanceCents;
                record.CompletedDeal = deal.Position == deal.TicketCount;
                _state.Ledger.PendingSpin = record;
                _state.Ledger.LastSpin = record;

                try
                {
                    if (record.CompletedDeal)
                    {
                        _lifecycle.Complete(slot);
                        _state.SaveHistory();
                    }
                    _state.SaveLedger();
                }
                catch (Exception e)
                {
                    _log.Error("Spin could not be saved, so it is undone: " + e.Message);
                    _state.ReloadFromStore();
                    _loader.LoadActiveDeals();
                    return SpinOutcome.Reject(SpinRejection.SaveFailed, "The spin could not be saved: " + e.Message);
                }

                _state.AppendSpinSafely(record, _log);
                _scheduler.EnsureDeals(slot);
                return SpinOutcome.Accept(record.Copy());
            }
        }

        public SpinRecord GetUnshownResult()
        {
            lock (_sync) return _state.Ledger.PendingSpin?.Copy();
        }

        public void MarkResultShown(long spinId)
        {
            lock (_sync)
            {
                if (_closed || _state.Ledger.PendingSpin == null || _state.Ledger.PendingSpin.SpinId != spinId) return;
                _state.Ledger.PendingSpin = null;
                _state.SaveLedger();
            }
        }

        // ---- admin ------------------------------------------------------------------------------------------

        public IReadOnlyList<SlotView> GetSlots()
        {
            lock (_sync) return _state.Ledger.Slots.Select(ToView).ToList();
        }

        public IReadOnlyList<DealStatus> GetActiveDeals()
        {
            lock (_sync) return _state.Ledger.Slots.Where(s => s.Active != null).Select(s => s.Active.Copy()).ToList();
        }

        public IReadOnlyList<FinishedDealRecord> GetFinishedDeals()
        {
            lock (_sync) return PoolSerializer.Clone(_state.History.Finished);
        }

        public IReadOnlyList<RetireRecord> GetRetireLog()
        {
            lock (_sync) return PoolSerializer.Clone(_state.History.RetireLog);
        }

        public IReadOnlyList<SpinRecord> GetSpinLog()
        {
            lock (_sync) return _store.LoadSpinLog();
        }

        public RetireRecord Retire(string mode, long betCents, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required to retire a deal.", nameof(reason));
            lock (_sync)
            {
                ThrowIfClosed();
                var slot = _state.FindSlot(mode, betCents);
                if (slot?.Active == null) throw new InvalidOperationException("No active deal for " + mode + " " + Money.Format(betCents) + ".");

                var record = _lifecycle.Retire(slot, reason, requireReplacement: true);
                _state.SaveHistory();
                _state.SaveLedger();
                _scheduler.EnsureDeals(slot);
                return PoolSerializer.Clone(record);
            }
        }

        public void ApplyConfig(PoolConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            lock (_sync)
            {
                ThrowIfClosed();
                var previous = _state.Config.ConfigVersion;
                _state.Config = config;
                _state.AddMissingPlayers();
                foreach (var slot in _state.Ledger.Slots)
                {
                    slot.BuildError = null;
                    if (slot.Ready == null || slot.Ready.ConfigVersion == config.ConfigVersion) continue;
                    _lifecycle.Discard(slot.Ready, "built on config " + slot.Ready.ConfigVersion + ", the config changed to " + config.ConfigVersion);
                    slot.Ready = null;
                }
                _state.SaveHistory();
                _state.SaveLedger();
                _log.Info("Config " + previous + " -> " + config.ConfigVersion + ": active deals keep their version, successors are rebuilt.");
                EnsureEveryActiveSlotHasDeals();
            }
        }

        public void RetryBuild(string mode, long betCents)
        {
            lock (_sync)
            {
                ThrowIfClosed();
                var slot = _state.GetOrAddSlot(mode, betCents);
                slot.BuildError = null;
                slot.BuildErrorConfigVersion = null;
                _scheduler.EnsureDeals(slot);
            }
        }

        // ---- internals --------------------------------------------------------------------------------------

        private SpinOutcome CheckSpin(string playerId, string mode, long betCents, out PlayerState player, out SlotState slot)
        {
            player = null;
            slot = null;
            if (_state.Ledger.PendingSpin != null)
                return SpinOutcome.Reject(SpinRejection.PreviousResultNotShown, "The previous result has not been shown yet.");

            player = _state.FindPlayer(playerId);
            if (player == null) return SpinOutcome.Reject(SpinRejection.UnknownPlayer, "Unknown player '" + playerId + "'.");
            if (!_state.Config.HasMode(mode)) return SpinOutcome.Reject(SpinRejection.UnknownMode, "Unknown mode '" + mode + "'.");
            if (!_state.Config.HasBetLevel(betCents)) return SpinOutcome.Reject(SpinRejection.UnknownBet, "Unknown bet " + Money.Format(betCents) + ".");

            slot = _state.GetOrAddSlot(mode, betCents);
            if (slot.Active == null)
            {
                _scheduler.EnsureDeals(slot);
                var why = slot.BuildError != null ? "The deal was rejected: " + slot.BuildError : "The deal for this mode and bet is being prepared.";
                return SpinOutcome.Reject(SpinRejection.DealNotReady, why);
            }

            if (player.BalanceCents < betCents)
                return SpinOutcome.Reject(SpinRejection.InsufficientBalance,
                    "Balance " + Money.Format(player.BalanceCents) + " is below the bet " + Money.Format(betCents) + ".");
            return null;
        }

        private void EnsureEveryActiveSlotHasDeals()
        {
            foreach (var slot in _state.Ledger.Slots.Where(s => s.Active != null)) _scheduler.EnsureDeals(slot);
        }

        private static PlayerView ToView(PlayerState player) => ViewMapper.ToView(player);
        private SlotView ToView(SlotState slot) => ViewMapper.ToView(slot, _scheduler.IsBuilding(slot));
    }
}
