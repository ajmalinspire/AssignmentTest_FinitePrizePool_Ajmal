using System.IO;
using FinitePrizePool.Engine.Persistence;
using NUnit.Framework;

namespace FinitePrizePool.Tests
{
    /// <summary>Crash-safe saving (§3.3).</summary>
    public class PersistenceTests
    {
        private string _folder;

        [SetUp]
        public void SetUp() => _folder = Services.TempFolder();

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Test]
        public void AtomicWrite_ReplacesTheFile_AndKeepsThePreviousVersion()
        {
            var path = Path.Combine(_folder, "file.json");
            AtomicFile.WriteAllText(path, "one");
            AtomicFile.WriteAllText(path, "two");

            Assert.AreEqual("two", AtomicFile.ReadAllText(path));
            Assert.AreEqual("one", AtomicFile.ReadBackup(path));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }

        [Test]
        public void CrashWhileWritingTheTempFile_LeavesTheSavedFileIntact()
        {
            var path = Path.Combine(_folder, "file.json");
            AtomicFile.WriteAllText(path, "saved");
            File.WriteAllText(path + ".tmp", "half-writ"); // a crash before the swap

            Assert.AreEqual("saved", AtomicFile.ReadAllText(path));
            AtomicFile.WriteAllText(path, "next");
            Assert.AreEqual("next", AtomicFile.ReadAllText(path));
        }

        [Test]
        public void UnreadableLedger_IsRestoredFromItsBackup()
        {
            var log = new CapturingLog();
            var store = new JsonFileStore(_folder, log);
            store.SaveLedger(new Ledger { NextSpinId = 5 });
            store.SaveLedger(new Ledger { NextSpinId = 6 });
            File.WriteAllText(Path.Combine(_folder, "ledger.json"), "{ not json");

            var ledger = store.LoadLedger();

            Assert.AreEqual(5, ledger.NextSpinId);
            Assert.IsNotEmpty(log.Warnings);
        }

        [Test]
        public void TornLastLineOfTheSpinLog_IsSkipped()
        {
            var store = new JsonFileStore(_folder, new CapturingLog());
            store.AppendSpin(new SpinRecord { SpinId = 1 });
            store.AppendSpin(new SpinRecord { SpinId = 2 });
            File.AppendAllText(Path.Combine(_folder, "spins.jsonl"), "{\"spinId\":3,\"pla");

            Assert.AreEqual(2, store.LoadLastLoggedSpin().SpinId);
            Assert.AreEqual(2, store.LoadSpinLog().Count);
        }

        [Test]
        public void SpinMissingFromTheLog_IsAddedWhenTheGameOpens()
        {
            var store = new InMemoryStore();
            var service = Services.Open(store);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            service.SpinAndShow();
            var ledger = store.LoadLedger();
            ledger.LastSpin = new SpinRecord { SpinId = 99, DealId = "x" }; // as if the log append was lost in a crash
            store.SaveLedger(ledger);

            Services.Open(store);

            Assert.AreEqual(99, store.LoadLastLoggedSpin().SpinId);
        }
    }
}
