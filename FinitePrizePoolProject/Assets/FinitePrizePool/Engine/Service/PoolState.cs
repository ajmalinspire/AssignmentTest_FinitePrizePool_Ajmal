using System.Collections.Generic;
using System.Linq;
using FinitePrizePool.Engine.Building;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Service
{
    /// <summary>
    /// The service's in-memory state: the ledger, the history, the current config and the tickets of the
    /// active deals. Only code holding the service lock touches it.
    /// </summary>
    internal sealed class PoolState
    {
        private readonly IPoolStore _store;
        private readonly Dictionary<string, List<Ticket>> _activeTickets = new Dictionary<string, List<Ticket>>();

        public Ledger Ledger { get; private set; }
        public DealHistory History { get; private set; }
        public PoolConfig Config { get; set; }

        public PoolState(IPoolStore store, PoolConfig config)
        {
            _store = store;
            Config = config;
            ReloadFromStore();
        }

        /// <summary>Throws away in-memory changes and reads the last saved state again (used after a failed save).</summary>
        public void ReloadFromStore()
        {
            Ledger = _store.LoadLedger() ?? new Ledger();
            History = _store.LoadHistory() ?? new DealHistory();
            _activeTickets.Clear();
        }

        public PlayerState FindPlayer(string playerId) => Ledger.Players.FirstOrDefault(p => p.Id == playerId);

        public SlotState FindSlot(string mode, long betCents) =>
            Ledger.Slots.FirstOrDefault(s => s.Mode == mode && s.BetCents == betCents);

        public SlotState GetOrAddSlot(string mode, long betCents)
        {
            var slot = FindSlot(mode, betCents);
            if (slot != null) return slot;
            slot = new SlotState { Mode = mode, BetCents = betCents };
            Ledger.Slots.Add(slot);
            return slot;
        }

        /// <summary>Adds any test player from the config that the save does not have yet. Balances are never reset.</summary>
        public bool AddMissingPlayers()
        {
            var added = false;
            foreach (var player in Config.TestPlayers)
            {
                if (FindPlayer(player.Id) != null) continue;
                Ledger.Players.Add(new PlayerState { Id = player.Id, Name = player.Name, BalanceCents = player.StartingBalanceCents });
                added = true;
            }
            return added;
        }

        public string NextDealId() => "deal-" + (Ledger.NextDealNumber++).ToString("D6");

        public List<Ticket> TicketsOf(DealStatus deal) => _activeTickets.TryGetValue(deal.DealId, out var t) ? t : null;
        public void HoldTickets(string dealId, List<Ticket> tickets) => _activeTickets[dealId] = tickets;
        public void ReleaseTickets(string dealId) => _activeTickets.Remove(dealId);

        public void SaveLedger() => _store.SaveLedger(Ledger);
        public void SaveHistory() => _store.SaveHistory(History);

        /// <summary>Appends to the spin log. A failure is only logged: the ledger, already saved, holds the spin.</summary>
        public void AppendSpinSafely(SpinRecord spin, IEngineLog log)
        {
            try { _store.AppendSpin(spin); }
            catch (System.Exception e) { log.Error("Spin " + spin.SpinId + " is saved in the ledger but could not be added to the spin log: " + e.Message); }
        }
    }
}
