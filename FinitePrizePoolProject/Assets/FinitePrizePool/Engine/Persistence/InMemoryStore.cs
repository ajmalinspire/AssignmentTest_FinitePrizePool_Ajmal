using System.Collections.Generic;
using System.Linq;
using FinitePrizePool.Engine.Building;

namespace FinitePrizePool.Engine.Persistence
{
    /// <summary>
    /// Keeps saves in memory as JSON text, for tests and the simulator. Because everything goes through
    /// JSON, a service opened on the same store behaves exactly like a game reopened from disk.
    /// </summary>
    public sealed class InMemoryStore : IPoolStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, string> _deals = new Dictionary<string, string>();
        private readonly List<string> _spinLog = new List<string>();
        private string _ledger;
        private string _history;

        /// <summary>How many times the ledger was saved (lets tests check that a save happened).</summary>
        public int LedgerSaves { get; private set; }

        /// <summary>For tests: makes the next ledger save throw, to simulate a full or failing disk.</summary>
        public bool FailNextLedgerSave { get; set; }

        public Ledger LoadLedger()
        {
            lock (_gate) return _ledger == null ? null : PoolSerializer.FromJson<Ledger>(_ledger);
        }

        public void SaveLedger(Ledger ledger)
        {
            lock (_gate)
            {
                if (FailNextLedgerSave)
                {
                    FailNextLedgerSave = false;
                    throw new System.IO.IOException("Simulated save failure.");
                }
                _ledger = PoolSerializer.ToJson(ledger, indented: false);
                LedgerSaves++;
            }
        }

        public DealHistory LoadHistory()
        {
            lock (_gate) return _history == null ? null : PoolSerializer.FromJson<DealHistory>(_history);
        }

        public void SaveHistory(DealHistory history)
        {
            lock (_gate) _history = PoolSerializer.ToJson(history, indented: false);
        }

        public void SaveDeal(DealDocument deal)
        {
            var json = PoolSerializer.ToJson(deal, indented: false);
            lock (_gate) _deals[deal.DealId] = json;
        }

        public DealDocument LoadDeal(string dealId)
        {
            string json;
            lock (_gate)
            {
                if (!_deals.TryGetValue(dealId, out json)) throw new KeyNotFoundException("Deal not found: " + dealId);
            }
            return PoolSerializer.FromJson<DealDocument>(json);
        }

        public bool HasDeal(string dealId)
        {
            lock (_gate) return _deals.ContainsKey(dealId);
        }

        /// <summary>For tests: replace a saved deal's JSON to simulate tampering or corruption.</summary>
        public void OverwriteDealJson(string dealId, string json)
        {
            lock (_gate) _deals[dealId] = json;
        }

        public string ReadDealJson(string dealId)
        {
            lock (_gate) return _deals[dealId];
        }

        public void AppendSpin(SpinRecord spin)
        {
            var json = PoolSerializer.ToJson(spin, indented: false);
            lock (_gate) _spinLog.Add(json);
        }

        public SpinRecord LoadLastLoggedSpin()
        {
            lock (_gate) return _spinLog.Count == 0 ? null : PoolSerializer.FromJson<SpinRecord>(_spinLog[_spinLog.Count - 1]);
        }

        public IReadOnlyList<SpinRecord> LoadSpinLog()
        {
            lock (_gate) return _spinLog.Select(PoolSerializer.FromJson<SpinRecord>).ToList();
        }
    }
}
