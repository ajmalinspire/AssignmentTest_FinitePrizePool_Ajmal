using System.Linq;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Service
{
    /// <summary>
    /// Brings saved state back into play when the game opens: adds new test players, repairs the spin log,
    /// drops successors built on another config, and re-verifies every active deal from disk before it is
    /// played again. Call under the service lock.
    /// </summary>
    internal sealed class SavedStateLoader
    {
        private readonly PoolState _state;
        private readonly DealFactory _factory;
        private readonly DealLifecycle _lifecycle;
        private readonly IPoolStore _store;
        private readonly IEngineLog _log;

        public SavedStateLoader(PoolState state, DealFactory factory, DealLifecycle lifecycle, IPoolStore store, IEngineLog log)
        {
            _state = state;
            _factory = factory;
            _lifecycle = lifecycle;
            _store = store;
            _log = log;
        }

        public void Open()
        {
            _state.AddMissingPlayers();
            RepairSpinLog();

            foreach (var slot in _state.Ledger.Slots)
            {
                if (slot.Ready == null || slot.Ready.ConfigVersion == _state.Config.ConfigVersion) continue;
                _lifecycle.Discard(slot.Ready, "built on config " + slot.Ready.ConfigVersion + ", the game opened with " + _state.Config.ConfigVersion);
                slot.Ready = null;
            }

            LoadActiveDeals();
            _state.SaveHistory();
            _state.SaveLedger();
        }

        /// <summary>Loads and verifies the tickets of every active deal; a deal that fails is retired, never played.</summary>
        public void LoadActiveDeals()
        {
            foreach (var slot in _state.Ledger.Slots.Where(s => s.Active != null).ToList())
            {
                var check = _factory.LoadVerified(slot.Active.DealId);
                if (check.Succeeded)
                {
                    _state.HoldTickets(slot.Active.DealId, check.Tickets);
                    continue;
                }
                _lifecycle.Retire(slot, "Failed verification when the game opened: " + check.FailureReason, requireReplacement: false);
            }
        }

        /// <summary>The ledger is saved before the spin log, so a crash between the two can leave the last spin unlogged.</summary>
        private void RepairSpinLog()
        {
            var last = _state.Ledger.LastSpin;
            if (last == null) return;
            var logged = _store.LoadLastLoggedSpin();
            if (logged == null || logged.SpinId < last.SpinId) _state.AppendSpinSafely(last, _log);
        }
    }
}
