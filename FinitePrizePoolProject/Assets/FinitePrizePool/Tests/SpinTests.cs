using System.IO;
using System.Linq;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Service;
using NUnit.Framework;

namespace FinitePrizePool.Tests
{
    /// <summary>Spin handling and saving (§4.5, §3.3); the brief's scenarios 4, 5 and 6.</summary>
    public class SpinTests
    {
        private string _folder;

        [SetUp]
        public void SetUp() => _folder = Services.TempFolder();

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        private JsonFileStore DiskStore() => new JsonFileStore(_folder, new CapturingLog());

        // Scenario 4: a spin with too little balance is rejected and uses no ticket.
        [Test]
        public void SpinWithTooLittleBalance_IsRejected_AndUsesNoTicket()
        {
            var service = Services.Open(new InMemoryStore());
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);

            var outcome = service.Spin(TestIds.Poor, TestIds.Inner, TestIds.OneDollar); // 50 cents < $1.00

            Assert.IsFalse(outcome.Accepted);
            Assert.AreEqual(SpinRejection.InsufficientBalance, outcome.Rejection);
            Assert.AreEqual(50, service.GetPlayer(TestIds.Poor).BalanceCents);
            Assert.AreEqual(0, service.GetSlot(TestIds.Inner, TestIds.OneDollar).SpinsPlayed);
            Assert.IsNull(service.GetUnshownResult());
            Assert.AreEqual(0, service.SpinAndShow().TicketPosition, "the next accepted spin still gets the first ticket");
        }

        // Scenario 5: saving and reloading mid-deal continues from the exact next ticket, balances unchanged.
        [Test]
        public void ReopeningMidDeal_ContinuesFromTheExactNextTicket_WithBalancesUnchanged()
        {
            var first = Services.Open(DiskStore());
            first.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            for (var i = 0; i < 7; i++) first.SpinAndShow();
            var dealId = first.GetSlot(TestIds.Inner, TestIds.OneDollar).ActiveDealId;
            var balances = first.GetPlayers().ToDictionary(p => p.Id, p => p.BalanceCents);

            // A new service on the same folder is the game being closed and opened again.
            var reopened = Services.Open(DiskStore());

            CollectionAssert.AreEquivalent(balances, reopened.GetPlayers().ToDictionary(p => p.Id, p => p.BalanceCents));
            var slot = reopened.GetSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.AreEqual(dealId, slot.ActiveDealId);
            Assert.AreEqual(7, slot.SpinsPlayed);

            var next = reopened.SpinAndShow();
            var savedTicket = DiskStore().LoadDeal(dealId).Tickets[7];
            Assert.AreEqual(dealId, next.DealId);
            Assert.AreEqual(7, next.TicketPosition);
            Assert.AreEqual(savedTicket.AwardCents, next.AwardCents);
            Assert.AreEqual(balances[TestIds.Rich] - 100 + savedTicket.AwardCents, reopened.GetPlayer(TestIds.Rich).BalanceCents);
        }

        // Scenario 6: a spin taken but not shown (closed mid-animation) shows its original result after reload,
        // and does not use another ticket.
        [Test]
        public void SpinNotYetShown_ShowsItsOriginalResultAfterReload_WithoutUsingAnotherTicket()
        {
            var first = Services.Open(DiskStore());
            first.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            first.SpinAndShow();
            var taken = first.Spin(TestIds.Rich, TestIds.Inner, TestIds.OneDollar).Record; // the game closes before showing it
            var balanceAfterTaken = first.GetPlayer(TestIds.Rich).BalanceCents;

            var reopened = Services.Open(DiskStore());

            var unshown = reopened.GetUnshownResult();
            Assert.IsNotNull(unshown);
            Assert.AreEqual(taken.SpinId, unshown.SpinId);
            Assert.AreEqual(taken.TicketPosition, unshown.TicketPosition);
            Assert.AreEqual(taken.AwardCents, unshown.AwardCents);
            Assert.AreEqual(taken.PrizeType, unshown.PrizeType);
            Assert.AreEqual(2, reopened.GetSlot(TestIds.Inner, TestIds.OneDollar).SpinsPlayed, "no extra ticket was used");
            Assert.AreEqual(balanceAfterTaken, reopened.GetPlayer(TestIds.Rich).BalanceCents, "the award was credited once");

            var blocked = reopened.Spin(TestIds.Rich, TestIds.Inner, TestIds.OneDollar);
            Assert.AreEqual(SpinRejection.PreviousResultNotShown, blocked.Rejection);
            Assert.AreEqual(2, reopened.GetSlot(TestIds.Inner, TestIds.OneDollar).SpinsPlayed);

            reopened.MarkResultShown(unshown.SpinId);
            Assert.IsNull(reopened.GetUnshownResult());
            Assert.AreEqual(2, reopened.SpinAndShow().TicketPosition);
        }

        [Test]
        public void SpinRecord_IsSavedBeforeTheCallReturns()
        {
            var store = new InMemoryStore();
            var service = Services.Open(store);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);

            var record = service.Spin(TestIds.Rich, TestIds.Inner, TestIds.OneDollar).Record;

            var saved = store.LoadLedger().PendingSpin;
            Assert.AreEqual(record.SpinId, saved.SpinId);
            Assert.AreEqual(record.DealId, saved.DealId);
            Assert.AreEqual(record.TicketPosition, saved.TicketPosition);
            Assert.AreEqual(record.WagerCents, saved.WagerCents);
            Assert.AreEqual(record.AwardCents, saved.AwardCents);
            Assert.AreEqual(record.SpinId, store.LoadLastLoggedSpin().SpinId, "the spin log has it too");
        }

        [Test]
        public void FailedSave_UndoesTheSpin_AndUsesNoTicket()
        {
            var store = new InMemoryStore();
            var service = Services.Open(store);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            var balance = service.GetPlayer(TestIds.Rich).BalanceCents;

            store.FailNextLedgerSave = true;
            var outcome = service.Spin(TestIds.Rich, TestIds.Inner, TestIds.OneDollar);

            Assert.AreEqual(SpinRejection.SaveFailed, outcome.Rejection);
            Assert.AreEqual(balance, service.GetPlayer(TestIds.Rich).BalanceCents);
            Assert.AreEqual(0, service.GetSlot(TestIds.Inner, TestIds.OneDollar).SpinsPlayed);
            Assert.AreEqual(0, service.SpinAndShow().TicketPosition);
        }

        [Test]
        public void PlayersShareTheDeal_SwitchingPlayerDoesNotResetIt()
        {
            var service = Services.Open(new InMemoryStore(), TestConfig.WithTestPlayers().With(d => d.TestPlayers[1].StartingBalanceCents = 10000));
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);

            var a = service.SpinAndShow(playerId: TestIds.Rich);
            var b = service.SpinAndShow(playerId: TestIds.Poor);
            var c = service.SpinAndShow(playerId: TestIds.Rich);

            Assert.AreEqual(a.DealId, b.DealId);
            Assert.AreEqual(new long[] { 0, 1, 2 }, new[] { a.TicketPosition, b.TicketPosition, c.TicketPosition });
        }

        [Test]
        public void SpinIdIsSharedButEachPlayerCountsTheirOwnSpins_AcrossSlotsAndReopening()
        {
            var store = new InMemoryStore();
            var config = TestConfig.WithTestPlayers().With(d => d.TestPlayers[1].StartingBalanceCents = 10000);
            var service = Services.Open(store, config);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            service.PrepareSlot(TestIds.Outer, TestIds.OneDollar);

            var a = service.SpinAndShow(playerId: TestIds.Rich);
            var b = service.SpinAndShow(playerId: TestIds.Poor);
            var c = service.SpinAndShow(playerId: TestIds.Rich, mode: TestIds.Outer);

            Assert.AreEqual(new long[] { 1, 2, 3 }, new[] { a.SpinId, b.SpinId, c.SpinId });
            Assert.AreEqual(new long[] { 1, 1, 2 }, new[] { a.PlayerSpinNumber, b.PlayerSpinNumber, c.PlayerSpinNumber });

            var reopened = Services.Open(store, config);
            Assert.AreEqual(2, reopened.GetPlayer(TestIds.Rich).SpinCount);
            Assert.AreEqual(1, reopened.GetPlayer(TestIds.Poor).SpinCount);
        }

        [Test]
        public void FailedSave_DoesNotCountTheSpin()
        {
            var store = new InMemoryStore();
            var service = Services.Open(store);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            service.SpinAndShow();

            store.FailNextLedgerSave = true;
            service.Spin(TestIds.Rich, TestIds.Inner, TestIds.OneDollar);

            Assert.AreEqual(1, service.GetPlayer(TestIds.Rich).SpinCount);
            Assert.AreEqual(2, service.SpinAndShow().PlayerSpinNumber);
        }

        [Test]
        public void TamperedActiveDeal_IsRetiredWhenTheGameOpens()
        {
            var store = new InMemoryStore();
            var service = Services.Open(store);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            service.SpinAndShow();
            var dealId = service.GetSlot(TestIds.Inner, TestIds.OneDollar).ActiveDealId;
            var deal = store.LoadDeal(dealId);
            deal.Tickets.Last(t => t.AwardCents > 0).AwardCents *= 10;
            store.OverwriteDealJson(dealId, PoolSerializer.ToJson(deal));

            var reopened = Services.Open(store);

            var slot = reopened.GetSlot(TestIds.Inner, TestIds.OneDollar);
            Assert.AreNotEqual(dealId, slot.ActiveDealId);
            Assert.AreEqual(0, slot.SpinsPlayed);
            var retired = reopened.GetRetireLog().Single();
            Assert.AreEqual(dealId, retired.RetiredDealId);
            StringAssert.Contains("Failed verification", retired.Reason);
        }
    }
}
