using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Service;

namespace FinitePrizePool.Engine.Reports
{
    public sealed class SimulatedDeal
    {
        public string DealId;
        public long Spins;
        public long WagerCents;
        public long PayoutCents;
        public long PrizeHits;
        public long MaxLossRun;
        public bool ReachedTarget;
        public Rtp Rtp => new Rtp(PayoutCents, WagerCents);
    }

    public sealed class SimulationResult
    {
        public string Mode;
        public long BetCents;
        public string ConfigVersion;
        public int WindowSpins;
        public readonly List<SimulatedDeal> Deals = new List<SimulatedDeal>();

        /// <summary>RTP of every consecutive block of <see cref="WindowSpins"/> spins, in percent (§8.2 question 4).</summary>
        public readonly List<decimal> WindowRtpPercents = new List<decimal>();

        public TimeSpan Elapsed;
        public long TotalSpins => Deals.Sum(d => d.Spins);
    }

    /// <summary>
    /// Plays whole deals through the real <see cref="DealService"/>, with no animation, on an in-memory store
    /// (stretch goal, §6). Used for the simulation report (§7.4) and the Editor simulator window.
    /// </summary>
    public static class Simulator
    {
        private const string PlayerId = "simulator";

        public static SimulationResult PlayCompleteDeals(PoolConfig config, string mode, long betCents, int deals, int windowSpins = 50, IEngineLog log = null)
        {
            var simConfig = config.With(data => data.TestPlayers = new List<TestPlayerData>
            {
                new TestPlayerData { Id = PlayerId, Name = "Simulator", StartingBalanceCents = long.MaxValue / 4 },
                new TestPlayerData { Id = PlayerId + "-2", Name = "Simulator 2", StartingBalanceCents = 0 }
            });

            var service = new DealService(simConfig, new InMemoryStore(), new InlineBackgroundRunner(), log: log);
            service.PrepareSlot(mode, betCents);

            var result = new SimulationResult { Mode = mode, BetCents = betCents, ConfigVersion = config.ConfigVersion, WindowSpins = windowSpins };
            var timer = Stopwatch.StartNew();
            SimulatedDeal current = null;
            long lossRun = 0, windowSpinsSoFar = 0, windowWager = 0, windowPayout = 0;

            while (result.Deals.Count < deals)
            {
                var outcome = service.Spin(PlayerId, mode, betCents);
                if (!outcome.Accepted) throw new InvalidOperationException("Simulation stopped: " + outcome.Message);
                var spin = outcome.Record;
                service.MarkResultShown(spin.SpinId);

                if (current == null || current.DealId != spin.DealId)
                {
                    current = new SimulatedDeal { DealId = spin.DealId };
                    lossRun = 0;
                    windowSpinsSoFar = windowWager = windowPayout = 0;
                }

                current.Spins++;
                current.WagerCents += spin.WagerCents;
                current.PayoutCents += spin.AwardCents;
                if (spin.AwardCents > 0) { current.PrizeHits++; lossRun = 0; }
                else current.MaxLossRun = Math.Max(current.MaxLossRun, ++lossRun);

                windowSpinsSoFar++;
                windowWager += spin.WagerCents;
                windowPayout += spin.AwardCents;
                if (windowSpinsSoFar == windowSpins)
                {
                    result.WindowRtpPercents.Add(new Rtp(windowPayout, windowWager).Percent);
                    windowSpinsSoFar = windowWager = windowPayout = 0;
                }

                if (!spin.CompletedDeal) continue;
                var dealId = current.DealId;
                current.ReachedTarget = service.GetFinishedDeals().Single(d => d.DealId == dealId).ReachedTarget;
                result.Deals.Add(current);
                current = null;
            }

            result.Elapsed = timer.Elapsed;
            return result;
        }
    }
}
