using System;
using System.Linq;
using FinitePrizePool.Engine.Reports;
using NUnit.Framework;

namespace FinitePrizePool.Tests
{
    /// <summary>The simulator's real-world play: both players, random modes, bets and sessions, sharing deals.</summary>
    public class RealWorldSimulatorTests
    {
        private static RealWorldSettings Settings() => new RealWorldSettings
        {
            TargetCompletedDeals = 6,
            MinBetCents = 25,
            MaxBetCents = 100,
            MinSessionSpins = 5,
            MaxSessionSpins = 40,
            StartingBalanceCents = 100_000_000
        };

        [Test]
        public void CompletesTheTargetNumberOfDeals_EveryOneAtExactly90Percent()
        {
            var random = new Random(12345); // test-only, so the run is repeatable
            var result = RealWorldSimulator.Run(TestConfig.Load(), Settings(), next: random.Next);

            var completed = result.CompletedDeals.ToList();
            Assert.AreEqual(6, completed.Count, result.StopReason);
            Assert.IsTrue(completed.All(d => d.ReachedTarget && d.Rtp.IsExactly(9000) && d.Spins == d.TicketCount));
            Assert.AreEqual(result.Spins, result.Players.Sum(p => p.Spins), "every spin belongs to a player");
            Assert.AreEqual(result.Spins, result.Deals.Sum(d => d.Spins), "every spin belongs to a deal");
            Assert.IsTrue(result.Players.All(p => p.Spins > 0), "both players played");
            Assert.IsTrue(result.Deals.Any(d => d.SpinsByPlayer.Count > 1), "players shared at least one deal");
        }

        [Test]
        public void PlayersWhoCannotAffordTheLowestBet_RunOutAndTheRunStops()
        {
            var settings = Settings();
            settings.StartingBalanceCents = 500; // $5 each: gone in a few dozen spins
            settings.TargetCompletedDeals = 1000;

            var result = RealWorldSimulator.Run(TestConfig.Load(), settings, next: new Random(7).Next);

            Assert.AreEqual("every player ran out of balance", result.StopReason);
            Assert.IsTrue(result.Players.All(p => p.RanOut && p.EndBalanceCents < settings.MinBetCents));
        }
    }
}
