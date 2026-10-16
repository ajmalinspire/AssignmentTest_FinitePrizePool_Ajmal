using System.IO;
using System.Linq;
using FinitePrizePool.Engine.Building;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Placement;
using NUnit.Framework;

namespace FinitePrizePool.Tests
{
    /// <summary>Deal building (§4.3), placement (§4.4) and verification; the brief's scenarios 2 and 3.</summary>
    public class DealBuilderTests
    {
        private PoolConfig _config;
        private CapturingLog _log;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.WithTestPlayers();
            _log = new CapturingLog();
        }

        private BuildResult Build(string mode, long bet, PoolConfig config = null)
        {
            config = config ?? _config;
            var plan = new DealSizeCalculator(config).PlanSmallest(mode, bet);
            return new DealBuilder(new CryptoSecureRandom(), _log, new FixedClock()).Build("deal-test", plan, config);
        }

        // Scenario 2: no ticket of Regular (or Big) cycle n+1 comes before every ticket of cycle n has been used.
        [Test]
        public void CycleIsolation_EachCycleIsUsedUpBeforeTheNextStarts()
        {
            var result = Build(TestIds.Outer, TestIds.OneDollar); // 9 paired cycles
            Assert.IsTrue(result.Succeeded, result.FailureReason);
            var mode = _config.GetMode(TestIds.Outer);

            AssertIsolated(result.Deal, SlotCategory.Regular, mode.Regular);
            AssertIsolated(result.Deal, SlotCategory.Big, mode.Big);
        }

        private static void AssertIsolated(DealDocument deal, SlotCategory category, Bag bag)
        {
            var stream = deal.Tickets.Where(t => t.Category == category).ToList();
            Assert.AreEqual(deal.PairedCycles * bag.TicketCount, stream.Count);

            for (var cycle = 0; cycle < deal.PairedCycles; cycle++)
            {
                var block = stream.Skip(cycle * bag.TicketCount).Take(bag.TicketCount).ToList();
                Assert.IsTrue(block.All(t => t.Cycle == cycle), category + " cycle " + cycle + " is mixed with another cycle");
                CollectionAssert.AreEquivalent(bag.Tickets.ToList(), block.Select(t => t.Prize).ToList(),
                    category + " cycle " + cycle + " is not one full bag");
            }
        }

        // Scenario 3a: generated deals satisfy every §4.4 limit.
        [TestCase("Inner", 100L)]
        [TestCase("Inner", 475L)]
        [TestCase("Outer", 100L)]
        [TestCase("Outer", 175L)]
        public void GeneratedDeals_MeetEveryPlacementLimit(string mode, long bet)
        {
            for (var i = 0; i < 5; i++)
            {
                var result = Build(mode, bet);
                Assert.IsTrue(result.Succeeded, result.FailureReason);

                var deal = result.Deal;
                var report = PlacementValidator.Validate(deal.Tickets.Select(t => t.Category).ToList(), deal.Limits,
                    deal.RegularTickets, deal.BigTickets, deal.LosingTickets);
                Assert.IsTrue(report.IsValid, string.Join("; ", report.Violations));
                Assert.LessOrEqual(report.MaxLossRun, deal.Limits.MaxConsecutiveLosses);
                Assert.LessOrEqual(report.MaxRegularGap, deal.Limits.MaxRegularGap);
                Assert.LessOrEqual(report.MaxBigGap, deal.Limits.MaxBigGap);
                Assert.IsFalse(report.HasRepeatingInterval);
            }
        }

        // Scenario 3b: Inner $1.00 with max consecutive losses set to 5 is rejected as impossible, before any attempt.
        [Test]
        public void InnerOneDollar_WithMaxFiveLossesInARow_IsRejectedAsImpossible()
        {
            var tight = _config.With(d => d.Placement.MaxConsecutiveLossesOverride = 5);

            var result = Build(TestIds.Inner, TestIds.OneDollar, tight);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, result.Attempts, "impossible limits must be rejected before generating");
            StringAssert.Contains("impossible", result.FailureReason);
            Assert.AreEqual(1, _log.Errors.Count, "the rejection is logged");
        }

        [Test]
        public void LimitsThatCannotBeMetInTime_AreRejectedAfterTheAttemptLimit_NotLoosened()
        {
            // Possible on paper, but a plain shuffle will not find it: forces the attempt limit to be reached.
            var strict = _config.With(d =>
            {
                d.Placement.Strategy = PlacementStrategyNames.ShuffleAndCheck;
                d.MaxGenerationAttempts = 25;
            });

            var result = Build(TestIds.Outer, TestIds.OneDollar, strict);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(25, result.Attempts);
            StringAssert.Contains("25 attempts", result.FailureReason);
            Assert.AreEqual(1, _log.Errors.Count);
        }

        [Test]
        public void ShuffleAndCheck_StillBuildsTheSmallDeals()
        {
            var literal = _config.With(d => d.Placement.Strategy = PlacementStrategyNames.ShuffleAndCheck);

            var result = Build(TestIds.Inner, TestIds.OneDollar, literal);

            Assert.IsTrue(result.Succeeded, result.FailureReason);
            Assert.IsTrue(DealVerifier.Verify(result.Deal).IsValid);
        }

        [Test]
        public void EveryModeAndBet_BuildsAndVerifies()
        {
            foreach (var plan in new DealSizeCalculator(_config).PlanAll())
            {
                var result = Build(plan.Mode, plan.BetCents);
                Assert.IsTrue(result.Succeeded, plan + ": " + result.FailureReason);
                var verification = DealVerifier.Verify(result.Deal);
                Assert.IsTrue(verification.IsValid, plan + ": " + string.Join("; ", verification.Problems));
            }
        }

        [Test]
        public void Verifier_CatchesAChangedAward()
        {
            var deal = Build(TestIds.Inner, TestIds.OneDollar).Deal;
            deal.Tickets.First(t => t.Kind != PrizeKind.Loss).AwardCents += 1;

            var verification = DealVerifier.Verify(deal);

            Assert.IsFalse(verification.IsValid);
        }

        [Test]
        public void Verifier_CatchesTicketsMixedAcrossCycles()
        {
            var deal = Build(TestIds.Outer, TestIds.OneDollar).Deal;
            var regular = deal.Tickets.Where(t => t.Category == SlotCategory.Regular).ToList();
            var first = regular.First(t => t.Cycle == 0);
            var later = regular.First(t => t.Cycle == 1);
            SwapContents(first, later);

            var verification = DealVerifier.Verify(deal);

            Assert.IsFalse(verification.IsValid);
            Assert.IsTrue(verification.Problems.Any(p => p.Contains("cycle")), string.Join("; ", verification.Problems));
        }

        [Test]
        public void Verifier_CatchesABrokenPlacement()
        {
            var deal = Build(TestIds.Inner, TestIds.OneDollar).Deal;
            var tight = new PlacementLimits { MaxConsecutiveLosses = 1, MaxRegularGap = deal.Limits.MaxRegularGap, MaxBigGap = deal.Limits.MaxBigGap };
            deal.Limits = tight;

            Assert.IsFalse(DealVerifier.Verify(deal).IsValid);
        }

        [Test]
        public void RepeatingInterval_IsDetected()
        {
            Assert.IsTrue(PlacementValidator.IntervalsRepeat(new[] { 0, 5, 10, 15, 20 }));         // every 5
            Assert.IsTrue(PlacementValidator.IntervalsRepeat(new[] { 0, 2, 7, 9, 14, 16, 21 }));   // 2,5 repeating
            Assert.IsFalse(PlacementValidator.IntervalsRepeat(new[] { 0, 3, 4, 9, 11, 18 }));
        }

        [Test]
        public void Engine_NeverUsesSystemRandomOrUnityRandom()
        {
            var engineFolder = TestPaths.FindUp(TestPaths.EngineRelativePath);
            foreach (var file in Directory.GetFiles(engineFolder, "*.cs", SearchOption.AllDirectories))
            {
                var code = File.ReadAllText(file);
                StringAssert.DoesNotContain("System.Random", code, file);
                StringAssert.DoesNotContain("UnityEngine.Random", code, file);
                StringAssert.DoesNotContain("new Random(", code, file);
            }
        }

        private static void SwapContents(Ticket a, Ticket b)
        {
            var kind = a.Kind; var multiplier = a.Multiplier; var bonus = a.BonusUnits; var award = a.AwardCents;
            a.Kind = b.Kind; a.Multiplier = b.Multiplier; a.BonusUnits = b.BonusUnits; a.AwardCents = b.AwardCents; a.Cycle = 1;
            b.Kind = kind; b.Multiplier = multiplier; b.BonusUnits = bonus; b.AwardCents = award; b.Cycle = 0;
        }
    }
}
