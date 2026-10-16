using System.Linq;
using FinitePrizePool.Engine.Building;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Reports;
using NUnit.Framework;

namespace FinitePrizePool.Tests
{
    /// <summary>Deal sizes (§4.2) and the brief's scenarios 1 and 10.</summary>
    public class DealMathTests
    {
        private PoolConfig _config;
        private DealSizeCalculator _calculator;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.WithTestPlayers();
            _calculator = new DealSizeCalculator(_config);
        }

        // Scenario 1: Inner $1.00 has the right counts, wagers and payouts, and is exactly 90% once fully used.
        [Test]
        public void InnerOneDollar_HasCorrectCounts_AndPaysExactly90PercentWhenUsedUp()
        {
            var mode = _config.GetMode(TestIds.Inner);

            // Independent arithmetic, straight from the bags: one paired cycle pays (multipliers × $1) + (bonus units × $1),
            // and every paid spin at 90% RTP is worth 90 cents of payout.
            var payoutPerCycle = (long)((mode.Regular.MultiplierTotal + mode.Big.MultiplierTotal) * 100)
                                 + (mode.Regular.BonusUnitsTotal + mode.Big.BonusUnitsTotal) * 100;
            Assert.AreEqual(0, payoutPerCycle % 90, "Inner $1.00 must fit in one paired cycle (brief §8.2 Q1)");
            var expectedSpins = payoutPerCycle / 90;
            var expectedPrizes = mode.Regular.TicketCount + mode.Big.TicketCount;

            var plan = _calculator.PlanSmallest(TestIds.Inner, TestIds.OneDollar);
            Assert.IsTrue(plan.IsValid, plan.RejectionReason);
            Assert.AreEqual(1, plan.PairedCycles);
            Assert.AreEqual(payoutPerCycle, plan.PayoutPerPairedCycleCents);
            Assert.AreEqual(expectedSpins, plan.PaidSpins);
            Assert.AreEqual(expectedPrizes, plan.PrizeTickets);
            Assert.AreEqual(expectedSpins - expectedPrizes, plan.LosingTickets);
            Assert.AreEqual(expectedSpins * 100, plan.TotalWagerCents);
            Assert.AreEqual(payoutPerCycle, plan.TotalPayoutCents);
            Assert.IsTrue(plan.PlannedRtp.IsExactly(9000));

            // Play the whole deal through the service and check what was actually paid.
            var service = Services.Open(new InMemoryStore(), _config);
            service.PrepareSlot(TestIds.Inner, TestIds.OneDollar);
            long wagered = 0, paid = 0, losses = 0;
            for (var i = 0; i < plan.PaidSpins; i++)
            {
                var spin = service.SpinAndShow();
                wagered += spin.WagerCents;
                paid += spin.AwardCents;
                if (spin.PrizeType == PrizeKind.Loss) losses++;
            }

            Assert.AreEqual(plan.LosingTickets, losses);
            Assert.AreEqual(plan.TotalWagerCents, wagered);
            Assert.AreEqual(plan.TotalPayoutCents, paid);
            Assert.IsTrue(new Rtp(paid, wagered).IsExactly(9000), "actual RTP " + new Rtp(paid, wagered));

            var record = service.GetFinishedDeals().Single();
            Assert.AreEqual(DealState.Completed, record.State);
            Assert.IsTrue(record.ReachedTarget);
            Assert.AreEqual(90m, record.ActualRtpPercent);
        }

        // The numbers the assumed bags give. Update them when the real bags from the maths PDF are in the config.
        [Test, Category("AssumedBags")]
        public void AssumedBags_GiveTheExpectedDealSizes()
        {
            var inner = _calculator.PlanSmallest(TestIds.Inner, 100);
            Assert.AreEqual(new long[] { 1, 80, 11, 69 }, new[] { inner.PairedCycles, inner.PaidSpins, inner.PrizeTickets, inner.LosingTickets });

            var outer = _calculator.PlanSmallest(TestIds.Outer, 100);
            Assert.AreEqual(new long[] { 9, 2380, 162, 2218 }, new[] { outer.PairedCycles, outer.PaidSpins, outer.PrizeTickets, outer.LosingTickets });

            var outerBig = _calculator.PlanSmallest(TestIds.Outer, 475);
            Assert.AreEqual(171, outerBig.PairedCycles);
            Assert.AreEqual(41170, outerBig.PaidSpins);
        }

        [Test]
        public void OuterOneDollar_NeedsNinePairedCycles()
        {
            Assert.AreEqual(9, _calculator.PlanSmallest(TestIds.Outer, TestIds.OneDollar).PairedCycles);
        }

        // Scenario 10: a size that would need a fractional number of tickets is rejected, never rounded.
        [Test]
        public void OuterAtOneSeventyFive_WithOnePairedCycle_IsRejectedNotRounded()
        {
            var plan = _calculator.PlanWithCycles(TestIds.Outer, 175, 1);

            Assert.IsFalse(plan.IsValid);
            StringAssert.Contains("not a whole number", plan.RejectionReason);

            var smallest = _calculator.PlanSmallest(TestIds.Outer, 175);
            Assert.IsTrue(smallest.IsValid);
            Assert.Greater(smallest.PairedCycles, 1);
            Assert.IsTrue(smallest.PlannedRtp.IsExactly(9000));
        }

        [Test]
        public void EverySmallestPlan_IsTheSmallest_AndExact()
        {
            foreach (var plan in _calculator.PlanAll())
            {
                Assert.IsTrue(plan.IsValid, plan.ToString());
                Assert.IsTrue(plan.PlannedRtp.IsExactly(9000), plan.ToString());
                Assert.GreaterOrEqual(plan.PaidSpins, plan.PrizeTickets, plan.ToString());
                if (plan.PairedCycles > 1)
                    Assert.IsFalse(_calculator.PlanWithCycles(plan.Mode, plan.BetCents, plan.PairedCycles - 1).IsValid,
                        plan + " is not the smallest: one cycle fewer also fits");
            }
        }

        [Test]
        public void Table_HasFortyRows_AndExportsCsv()
        {
            var rows = DealSizeTable.Build(_config);
            Assert.AreEqual(40, rows.Count);
            Assert.IsTrue(rows.All(r => r.Status == "OK"), string.Join("\n", rows.Where(r => r.Status != "OK").Select(r => r.Plan + " " + r.Status)));

            var csv = DealSizeTable.ToCsv(rows, _config);
            Assert.AreEqual(41, csv.Trim().Split('\n').Length, "header + 40 rows");
        }

        [Test]
        public void PaidSpinsFewerThanPrizeTickets_IsRejected()
        {
            // A bag of many tiny prizes cannot be paid for at a high bet: it would need fewer spins than prizes.
            var config = _config.With(d => d.Modes[0].RegularBag = new System.Collections.Generic.List<BagEntryData>
            {
                new BagEntryData { BonusUnits = 1, Count = 200 }
            });
            var plan = new DealSizeCalculator(config).PlanSmallest(TestIds.Inner, 500);

            Assert.IsFalse(plan.IsValid);
            StringAssert.Contains("fewer than", plan.RejectionReason);
        }

        [Test]
        public void MultiplierThatIsNotWholeCents_IsRejected()
        {
            var config = _config.With(d => d.Modes[0].RegularBag.Add(new BagEntryData { Multiplier = 0.5m, Count = 1 }));
            var plan = new DealSizeCalculator(config).PlanSmallest(TestIds.Inner, 25);

            Assert.IsFalse(plan.IsValid);
            StringAssert.Contains("not a whole number of cents", plan.RejectionReason);
        }

        [Test]
        public void ToleranceMode_UsesOnePairedCycleWherePossible_AndRecordsPlannedRtp()
        {
            var config = _config.With(d => d.RtpMode = RtpMode.Tolerance);
            var calculator = new DealSizeCalculator(config);

            var plan = calculator.PlanSmallest(TestIds.Outer, 475);
            Assert.IsTrue(plan.IsValid, plan.RejectionReason);
            Assert.AreEqual(1, plan.PairedCycles);
            Assert.That(plan.PlannedRtp.Percent, Is.InRange(89.9m, 90.1m));

            var service = Services.Open(new InMemoryStore(), TestConfig.WithTestPlayers(config));
            service.PrepareSlot(TestIds.Outer, 475);
            for (var i = 0; i < plan.PaidSpins; i++) service.SpinAndShow(TestIds.Outer, 475);
            var record = service.GetFinishedDeals().Single();
            Assert.IsTrue(record.ReachedTarget);
            Assert.AreEqual(record.PlannedRtpPercent, record.ActualRtpPercent);
        }
    }
}
