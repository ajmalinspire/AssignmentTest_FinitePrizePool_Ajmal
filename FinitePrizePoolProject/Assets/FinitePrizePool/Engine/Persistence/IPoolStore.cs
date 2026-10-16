using System.Collections.Generic;
using FinitePrizePool.Engine.Building;

namespace FinitePrizePool.Engine.Persistence
{
    /// <summary>
    /// Where the engine keeps its data. The game uses JSON files on the device; tests and the simulator use
    /// memory. Moving the engine to a server means writing one more implementation (a database), nothing else.
    /// </summary>
    public interface IPoolStore
    {
        /// <summary>Null when nothing has been saved yet.</summary>
        Ledger LoadLedger();
        void SaveLedger(Ledger ledger);

        /// <summary>Null when nothing has been saved yet.</summary>
        DealHistory LoadHistory();
        void SaveHistory(DealHistory history);

        /// <summary>Saves a complete deal once, before its first use. Deal files are never rewritten.</summary>
        void SaveDeal(DealDocument deal);
        DealDocument LoadDeal(string dealId);

        /// <summary>Appends to the spin log. The ledger (saved first) is the source of truth.</summary>
        void AppendSpin(SpinRecord spin);
        SpinRecord LoadLastLoggedSpin();
        IReadOnlyList<SpinRecord> LoadSpinLog();
    }
}
