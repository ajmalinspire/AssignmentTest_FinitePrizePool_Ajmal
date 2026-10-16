using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FinitePrizePool.Engine.Building;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Service;
using NUnit.Framework;

namespace FinitePrizePool.Tests
{
    /// <summary>The deal lifecycle (§4.6) and retire (§4.7); the brief's scenarios 7, 8 and 9.</summary>
    public class LifecycleTests
    {
        private static long DealSize(DealService service, string dealId) =>
            service.GetActiveDeals().Single(d => d.DealId == dealId).TicketCount;

        // Scenario 7: the final ticket completes the deal and play continues on the Ready successor.
        [Test]
        public void FinalTicket_CompletesTheDeal_AndPlayContinuesOnTheReadySuccessor()
        {
            var service = Services.Open(new InMemoryStore());
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            var before = service.GetSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.IsNotNull(before.ReadyDealId, "a verified successor is waiting before the deal ends");
            var size = DealSize(service, before.ActiveDealId);

            SpinRecord last = null;
            for (var i = 0; i < size; i++) last = service.SpinAndShow();

            Assert.IsTrue(last.CompletedDeal);
            var completed = service.GetFinishedDeals().Single(d => d.DealId == before.ActiveDealId);
            Assert.AreEqual(DealState.Completed, completed.State);
            Assert.AreEqual(size, completed.TicketCount);
            Assert.AreEqual(size * 100, completed.TotalWagerCents);
            Assert.AreEqual(9000, completed.TargetRtpBasisPoints);
            Assert.AreEqual(90m, completed.ActualRtpPercent);
            Assert.IsTrue(completed.ReachedTarget);

            var after = service.GetSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.AreEqual(before.ReadyDealId, after.ActiveDealId, "the successor took over");
            Assert.AreEqual(DealState.Active, after.ActiveState);
            Assert.IsNotNull(after.ReadyDealId, "and a new successor is ready behind it");

            var next = service.SpinAndShow();
            Assert.AreEqual(before.ReadyDealId, next.DealId);
            Assert.AreEqual(0, next.TicketPosition);
        }

        // Scenario 8: a config change during a deal leaves the active deal alone; the next deal uses the new version.
        [Test]
        public void ConfigChangeDuringADeal_LeavesTheActiveDeal_AndTheNextDealUsesTheNewVersion()
        {
            var service = Services.Open(new InMemoryStore());
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            for (var i = 0; i < 10; i++) service.SpinAndShow();
            var before = service.GetSlot(TestIds.Inner, TestIds.OneDollar);
            var oldVersion = before.ActiveConfigVersion;

            service.ApplyConfig(TestConfig.WithTestPlayers().With(d =>
            {
                d.ConfigVersion = "test-v2";
                d.Placement.LimitFactor = 3;
            }));

            var during = service.GetSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.AreEqual(before.ActiveDealId, during.ActiveDealId);
            Assert.AreEqual(oldVersion, during.ActiveConfigVersion);
            Assert.AreEqual(10, during.SpinsPlayed);
            Assert.AreNotEqual(before.ReadyDealId, during.ReadyDealId, "the old-version successor was rebuilt");
            Assert.AreEqual("test-v2", during.ReadyConfigVersion);
            var discarded = service.GetFinishedDeals().Single(d => d.DealId == before.ReadyDealId);
            Assert.AreEqual(DealState.Retired, discarded.State);
            Assert.AreEqual(0, discarded.TicketsUsed);
            Assert.IsFalse(discarded.ReachedTarget);

            var remaining = DealSize(service, before.ActiveDealId) - 10;
            for (var i = 0; i < remaining; i++)
                Assert.AreEqual(oldVersion, service.SpinAndShow().ConfigVersion, "the active deal keeps its version to the end");

            var next = service.SpinAndShow();
            Assert.AreEqual("test-v2", next.ConfigVersion);
            Assert.AreEqual(during.ReadyDealId, next.DealId);
        }

        // Scenario 9: retire logs every required field, and the replacement deal is fresh and verified.
        [Test]
        public void Retire_LogsEveryRequiredField_AndSwitchesToAFreshVerifiedReplacement()
        {
            var store = new InMemoryStore();
            var clock = new FixedClock();
            var service = Services.Open(store, clock: clock);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            long paid = 0;
            for (var i = 0; i < 10; i++) paid += service.SpinAndShow().AwardCents;
            var before = service.GetSlot(TestIds.Inner, TestIds.OneDollar);
            var size = DealSize(service, before.ActiveDealId);
            clock.UtcNow = clock.UtcNow.AddMinutes(5);

            var record = service.Retire(TestIds.Inner, TestIds.OneDollar, "Prize table correction");

            Assert.AreEqual(before.ActiveDealId, record.RetiredDealId);
            Assert.AreEqual("Prize table correction", record.Reason);
            Assert.AreEqual(clock.UtcNow, record.TimeUtc);
            Assert.AreEqual(before.ActiveConfigVersion, record.OldConfigVersion);
            Assert.AreEqual(service.ConfigVersion, record.NewConfigVersion);
            Assert.AreEqual(before.ReadyDealId, record.ReplacementDealId);
            Assert.AreEqual(10, record.TicketsUsed);
            Assert.AreEqual(size - 10, record.TicketsRemaining);
            Assert.AreEqual(1000, record.WagerCents);
            Assert.AreEqual(paid, record.PayoutCents);
            Assert.AreEqual(new Rtp(paid, 1000).Percent, record.ActualRtpPercent);
            Assert.IsFalse(record.ReachedTarget);
            Assert.AreEqual(1, service.GetRetireLog().Count);

            var finished = service.GetFinishedDeals().Single(d => d.DealId == record.RetiredDealId);
            Assert.AreEqual(DealState.Retired, finished.State);
            Assert.IsFalse(finished.ReachedTarget, "a retired deal is never reported as having reached its target");
            Assert.AreEqual(size, store.LoadDeal(record.RetiredDealId).Tickets.Count, "unused tickets are kept for audit");

            var after = service.GetSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.AreEqual(record.ReplacementDealId, after.ActiveDealId);
            Assert.AreEqual(DealState.Active, after.ActiveState);
            Assert.AreEqual(0, after.SpinsPlayed, "the replacement is fresh");
            Assert.IsTrue(DealVerifier.Verify(store.LoadDeal(after.ActiveDealId)).IsValid, "and verified");
            Assert.IsNotNull(after.ReadyDealId, "and has its own successor");
            Assert.AreEqual(0, service.SpinAndShow().TicketPosition);
        }

        [Test]
        public void RetireReason_IsRequired()
        {
            var service = Services.Open(new InMemoryStore());
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.Throws<ArgumentException>(() => service.Retire(TestIds.Inner, TestIds.OneDollar, " "));
        }

        [Test]
        public void SuccessorsAreBuiltInTheBackground_SpinsDoNotWaitForThem()
        {
            var runner = new QueuedRunner();
            var service = Services.Open(new InMemoryStore(), runner: runner);

            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.AreEqual(1, runner.Pending, "the first deal is queued, not built on the caller's thread");
            Assert.AreEqual(SpinRejection.DealNotReady, service.Spin(TestIds.Rich, TestIds.Inner, TestIds.OneDollar).Rejection);

            runner.RunAll(); // first deal, then its successor
            var slot = service.GetSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.IsNotNull(slot.ActiveDealId);
            Assert.IsNotNull(slot.ReadyDealId);

            var size = DealSize(service, slot.ActiveDealId);
            for (var i = 0; i < size; i++) service.SpinAndShow();
            Assert.AreEqual(1, runner.Pending, "the next successor is queued while play goes on");
            Assert.AreEqual(slot.ReadyDealId, service.SpinAndShow().DealId, "and play did not wait for it");
        }

        [Test]
        public void RealBackgroundThread_BuildsTheDeals()
        {
            var service = Services.Open(new InMemoryStore(), runner: new TaskBackgroundRunner(new CapturingLog()));
            service.PrepareSlot(TestIds.Outer, 475); // the largest deal

            var timer = Stopwatch.StartNew();
            while (service.GetSlot(TestIds.Outer, 475).ReadyDealId == null && timer.Elapsed < TimeSpan.FromSeconds(60))
                Thread.Sleep(20);

            Assert.IsNotNull(service.GetSlot(TestIds.Outer, 475).ActiveDealId);
            Assert.IsNotNull(service.GetSlot(TestIds.Outer, 475).ReadyDealId);
        }

        [Test]
        public void BuildFinishingAfterTheGameClosed_WritesNothing()
        {
            var store = new InMemoryStore();
            var runner = new QueuedRunner();
            var service = Services.Open(store, runner: runner);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            var savesBefore = store.LedgerSaves;

            service.Dispose();
            runner.RunAll();

            Assert.AreEqual(savesBefore, store.LedgerSaves, "a closed service must not overwrite the next session's saves");
            Assert.Throws<ObjectDisposedException>(() => service.Spin(TestIds.Rich, TestIds.Inner, TestIds.OneDollar));
        }

        [Test]
        public void ImpossibleConfig_IsRecordedOnTheSlot_AndNotRetriedForever()
        {
            var runner = new QueuedRunner();
            var config = TestConfig.WithTestPlayers().With(d => d.Placement.MaxConsecutiveLossesOverride = 5);
            var service = Services.Open(new InMemoryStore(), config, runner);

            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            runner.RunAll();

            var slot = service.GetSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.IsNull(slot.ActiveDealId);
            StringAssert.Contains("impossible", slot.BuildError);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.AreEqual(0, runner.Pending, "the same config is not retried");
            Assert.AreEqual(SpinRejection.DealNotReady, service.Spin(TestIds.Rich, TestIds.Inner, TestIds.OneDollar).Rejection);
        }
    }
}
