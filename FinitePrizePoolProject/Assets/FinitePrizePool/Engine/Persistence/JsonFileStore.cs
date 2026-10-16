using System;
using System.Collections.Generic;
using System.IO;
using FinitePrizePool.Engine.Building;
using FinitePrizePool.Engine.Core;

namespace FinitePrizePool.Engine.Persistence
{
    /// <summary>
    /// Saves to JSON files under one folder (in the game: Application.persistentDataPath/FinitePrizePool).
    ///
    ///   ledger.json        balances, deal positions, pending spin    rewritten atomically every spin
    ///   history.json       finished deals and the retire log          rewritten atomically when a deal ends
    ///   deals/{id}.json    the full ordered ticket list of one deal   written once, never changed
    ///   spins.jsonl        every spin record, one per line            appended after the ledger is saved
    /// </summary>
    public sealed class JsonFileStore : IPoolStore
    {
        private readonly IEngineLog _log;

        public string RootDirectory { get; }

        public JsonFileStore(string rootDirectory, IEngineLog log)
        {
            RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            _log = log ?? new NullEngineLog();
            Directory.CreateDirectory(RootDirectory);
        }

        private string LedgerPath => Path.Combine(RootDirectory, "ledger.json");
        private string HistoryPath => Path.Combine(RootDirectory, "history.json");
        private string SpinLogPath => Path.Combine(RootDirectory, "spins.jsonl");
        private string DealPath(string dealId) => Path.Combine(RootDirectory, "deals", dealId + ".json");

        public Ledger LoadLedger() => LoadWithBackup<Ledger>(LedgerPath);
        public void SaveLedger(Ledger ledger) => AtomicFile.WriteAllText(LedgerPath, PoolSerializer.ToJson(ledger));

        public DealHistory LoadHistory() => LoadWithBackup<DealHistory>(HistoryPath);
        public void SaveHistory(DealHistory history) => AtomicFile.WriteAllText(HistoryPath, PoolSerializer.ToJson(history));

        public void SaveDeal(DealDocument deal) => AtomicFile.WriteAllText(DealPath(deal.DealId), PoolSerializer.ToJson(deal, indented: false));

        public DealDocument LoadDeal(string dealId)
        {
            var deal = LoadWithBackup<DealDocument>(DealPath(dealId));
            if (deal == null) throw new FileNotFoundException("Deal file not found for " + dealId, DealPath(dealId));
            return deal;
        }

        public void AppendSpin(SpinRecord spin)
        {
            File.AppendAllText(SpinLogPath, PoolSerializer.ToJson(spin, indented: false) + "\n");
        }

        public SpinRecord LoadLastLoggedSpin()
        {
            SpinRecord last = null;
            foreach (var spin in ReadSpinLines()) last = spin;
            return last;
        }

        public IReadOnlyList<SpinRecord> LoadSpinLog() => new List<SpinRecord>(ReadSpinLines());

        private IEnumerable<SpinRecord> ReadSpinLines()
        {
            if (!File.Exists(SpinLogPath)) yield break;
            foreach (var line in File.ReadLines(SpinLogPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var spin = TryParse<SpinRecord>(line);
                if (spin != null) yield return spin; // a torn last line from a crash mid-append is skipped
            }
        }

        /// <summary>Reads a file, falling back to the copy File.Replace kept if the main one cannot be read.</summary>
        private T LoadWithBackup<T>(string path) where T : class
        {
            var text = AtomicFile.ReadAllText(path);
            if (text != null)
            {
                var value = TryParse<T>(text);
                if (value != null) return value;
                _log.Warning("Could not read " + path + "; trying its backup.");
            }

            var backup = AtomicFile.ReadBackup(path);
            if (backup == null) return null;

            var restored = TryParse<T>(backup);
            if (restored != null) _log.Warning("Restored " + path + " from its backup.");
            return restored;
        }

        private static T TryParse<T>(string json) where T : class
        {
            try { return PoolSerializer.FromJson<T>(json); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }
    }
}
