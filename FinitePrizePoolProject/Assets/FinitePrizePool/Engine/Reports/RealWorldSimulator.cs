using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Service;

namespace FinitePrizePool.Engine.Reports
{
    /// <summary>How the simulated players behave in <see cref="RealWorldSimulator"/>.</summary>
    public sealed class RealWorldSettings
    {
        /// <summary>Stop once this many deals (any mode and bet) have been completed.</summary>
        public int TargetCompletedDeals = 20;

        /// <summary>Safety stop: Outer deals at high bets run to tens of thousands of spins.</summary>
        public long MaxSpins = 500_000;

        public bool IncludeInner = true;
        public bool IncludeOuter = true;
        public long MinBetCents = 25;
        public long MaxBetCents = 500;

        /// <summary>A session is one player on one mode and bet for a random number of spins in this range.</summary>
        public int MinSessionSpins = 10;
        public int MaxSessionSpins = 100;

        /// <summary>Each player's starting balance; 0 keeps the balances in the config.</summary>
        public long StartingBalanceCents = 1_000_000;
    }

    public sealed class SimulatedPlayer
    {
        public string Id;
        public string Name;
        public long StartingBalanceCents;
        public long EndBalanceCents;
        public long Spins;
        public int Sessions;
        public long WagerCents;
        public long PayoutCents;
        public long BiggestWinCents;

        /// <summary>True when the player could no longer afford the lowest allowed bet and stopped.</summary>
        public bool RanOut;

        public Rtp Rtp => new Rtp(PayoutCents, WagerCents);
    }

    public sealed class SimulatedSlotDeal
    {
        public string DealId;
        public string Mode;
        public long BetCents;
        public long TicketCount;
        public long Spins;
        public long WagerCents;
        public long PayoutCents;
        public bool Completed;
        public bool ReachedTarget;

        /// <summary>Spins played on this deal by each player: deals are shared, so usually more than one.</summary>
        public readonly Dictionary<string, long> SpinsByPlayer = new Dictionary<string, long>();

        public Rtp Rtp => new Rtp(PayoutCents, WagerCents);
    }

    public sealed class RealWorldResult
    {
        public string ConfigVersion;
        public RealWorldSettings Settings;
        public string StopReason;
        public long Spins;
        public int Sessions;
        public TimeSpan Elapsed;
        public readonly List<SimulatedPlayer> Players = new List<SimulatedPlayer>();

        /// <summary>Every deal that took at least one spin, in the order it was first played.</summary>
        public readonly List<SimulatedSlotDeal> Deals = new List<SimulatedSlotDeal>();

        /// <summary>Mode/bet combinations left out because their deal could not be built (for example, impossible limits).</summary>
        public readonly List<string> RejectedSlots = new List<string>();

        public IEnumerable<SimulatedSlotDeal> CompletedDeals => Deals.Where(d => d.Completed);
        public IEnumerable<SimulatedSlotDeal> UnfinishedDeals => Deals.Where(d => !d.Completed);
    }

    /// <summary>
    /// Real-world play through the real <see cref="DealService"/> on an in-memory store: the config's test players
    /// take turns in sessions on random modes and bets, sharing each mode/bet deal as on a device, until a number of
    /// deals have been completed. Shows that every completed deal pays exactly its target while individual players,
    /// and deals cut off mid-way, do not. Player choices use the same cryptographic generator as the deal shuffle
    /// (the engine allows no other random source, §3.2); tests can pass their own.
    /// </summary>
    public static class RealWorldSimulator
    {
        /// <param name="progress">Called every 1,000 spins with (spins, completed deals); return false to stop.</param>
        /// <param name="next">Returns a whole number from 0 to n - 1. Defaults to RandomNumberGenerator.GetInt32.</param>
        public static RealWorldResult Run(PoolConfig config, RealWorldSettings settings, Func<long, int, bool> progress = null,
            Func<int, int> next = null, IEngineLog log = null)
        {
            next = next ?? RandomNumberGenerator.GetInt32;
            var simConfig = settings.StartingBalanceCents > 0
                ? config.With(data => data.TestPlayers.ForEach(p => p.StartingBalanceCents = settings.StartingBalanceCents))
                : config;
            var service = new DealService(simConfig, new InMemoryStore(), new InlineBackgroundRunner(), log: log);
            var calculator = new DealSizeCalculator(simConfig);

            var result = new RealWorldResult { ConfigVersion = config.ConfigVersion, Settings = settings };
            foreach (var player in service.GetPlayers())
                result.Players.Add(new SimulatedPlayer { Id = player.Id, Name = player.Name, StartingBalanceCents = player.BalanceCents });

            var slots = AllowedSlots(simConfig, settings);
            var deals = new Dictionary<string, SimulatedSlotDeal>();
            var completed = 0;
            var timer = Stopwatch.StartNew();
            result.StopReason = "no mode and bet matched the settings";

            while (slots.Count > 0)
            {
                if (completed >= settings.TargetCompletedDeals) { result.StopReason = "target reached: " + completed + " deals completed"; break; }
                if (result.Spins >= settings.MaxSpins) { result.StopReason = "spin limit reached (" + settings.MaxSpins.ToString("N0") + ")"; break; }

                var playing = result.Players.Where(p => !p.RanOut).ToList();
                if (playing.Count == 0) { result.StopReason = "every player ran out of balance"; break; }

                // A session: one player, one mode and bet, a random number of spins.
                var player = playing[next(playing.Count)];
                var slot = slots[next(slots.Count)];
                var length = settings.MinSessionSpins + next(Math.Max(1, settings.MaxSessionSpins - settings.MinSessionSpins + 1));
                player.Sessions++;
                result.Sessions++;
                service.PrepareSlot(slot.Mode, slot.BetCents); // built at once: the runner is inline

                for (var i = 0; i < length && completed < settings.TargetCompletedDeals && result.Spins < settings.MaxSpins; i++)
                {
                    // A player who cannot afford the bet drops to the highest one they can afford in the same mode.
                    var balance = service.GetPlayer(player.Id).BalanceCents;
                    if (balance < slot.BetCents)
                    {
                        var affordable = slots.Where(s => s.Mode == slot.Mode && s.BetCents <= balance).ToList();
                        if (affordable.Count == 0)
                        {
                            player.RanOut = slots.All(s => s.BetCents > balance);
                            break;
                        }
                        slot = affordable.OrderByDescending(s => s.BetCents).First();
                        service.PrepareSlot(slot.Mode, slot.BetCents);
                    }

                    var outcome = service.Spin(player.Id, slot.Mode, slot.BetCents);
                    if (!outcome.Accepted)
                    {
                        if (outcome.Rejection != SpinRejection.DealNotReady) throw new InvalidOperationException("Simulation stopped: " + outcome.Message);
                        result.RejectedSlots.Add(slot.Mode + " " + Money.Format(slot.BetCents) + ": " + service.GetSlot(slot.Mode, slot.BetCents).BuildError);
                        slots.Remove(slot);
                        break;
                    }

                    var spin = outcome.Record;
                    service.MarkResultShown(spin.SpinId);
                    result.Spins++;
                    Record(player, spin);
                    if (!deals.TryGetValue(spin.DealId, out var deal))
                    {
                        deal = new SimulatedSlotDeal
                        {
                            DealId = spin.DealId, Mode = spin.Mode, BetCents = spin.BetCents,
                            TicketCount = calculator.PlanSmallest(spin.Mode, spin.BetCents).PaidSpins
                        };
                        deals.Add(spin.DealId, deal);
                        result.Deals.Add(deal);
                    }
                    Record(deal, player.Id, spin);
                    if (spin.CompletedDeal) completed++;

                    if (progress != null && result.Spins % 1000 == 0 && !progress(result.Spins, completed))
                    {
                        result.StopReason = "stopped by the user";
                        slots.Clear();
                        break;
                    }
                }
            }

            foreach (var finished in service.GetFinishedDeals())
                if (deals.TryGetValue(finished.DealId, out var deal))
                {
                    deal.TicketCount = finished.TicketCount;
                    deal.ReachedTarget = finished.ReachedTarget;
                }
            foreach (var player in result.Players) player.EndBalanceCents = service.GetPlayer(player.Id).BalanceCents;
            result.Elapsed = timer.Elapsed;
            service.Dispose();
            return result;
        }

        private sealed class Slot
        {
            public string Mode;
            public long BetCents;
        }

        private static List<Slot> AllowedSlots(PoolConfig config, RealWorldSettings settings)
        {
            return (from mode in config.ModeNames
                    where (mode == "Inner" && settings.IncludeInner) || (mode == "Outer" && settings.IncludeOuter)
                          || (mode != "Inner" && mode != "Outer")
                    from bet in config.BetLevelsCents
                    where bet >= settings.MinBetCents && bet <= settings.MaxBetCents
                    select new Slot { Mode = mode, BetCents = bet }).ToList();
        }

        private static void Record(SimulatedPlayer player, SpinRecord spin)
        {
            player.Spins++;
            player.WagerCents += spin.WagerCents;
            player.PayoutCents += spin.AwardCents;
            player.BiggestWinCents = Math.Max(player.BiggestWinCents, spin.AwardCents);
        }

        private static void Record(SimulatedSlotDeal deal, string playerId, SpinRecord spin)
        {
            deal.Spins++;
            deal.WagerCents += spin.WagerCents;
            deal.PayoutCents += spin.AwardCents;
            deal.SpinsByPlayer.TryGetValue(playerId, out var spins);
            deal.SpinsByPlayer[playerId] = spins + 1;
            if (spin.CompletedDeal) deal.Completed = true;
        }
    }
}
