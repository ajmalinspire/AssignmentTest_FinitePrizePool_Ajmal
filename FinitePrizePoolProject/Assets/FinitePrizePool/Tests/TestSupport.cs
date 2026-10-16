using System;
using System.Collections.Generic;
using System.IO;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Service;

namespace FinitePrizePool.Tests
{
    /// <summary>Finds the real config file, so the tests always run against the bags the game uses.</summary>
    public static class TestPaths
    {
        public const string ConfigRelativePath = "Assets/FinitePrizePool/Resources/FinitePrizePoolConfig.json";
        public const string EngineRelativePath = "Assets/FinitePrizePool/Engine";

        /// <summary>The Unity project root in the editor (the working directory), or the source tree outside Unity.</summary>
        public static string FindUp(string relativePath)
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, relativePath);
                    if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
                    var packaged = Path.Combine(dir.FullName, "Package", relativePath);
                    if (File.Exists(packaged) || Directory.Exists(packaged)) return packaged;
                    dir = dir.Parent;
                }
            }
            throw new FileNotFoundException("Could not find " + relativePath + " above " + Directory.GetCurrentDirectory());
        }
    }

    public static class TestConfig
    {
        private static PoolConfig _cached;

        public static PoolConfig Load()
        {
            return _cached ?? (_cached = ConfigJson.Load(File.ReadAllText(TestPaths.FindUp(TestPaths.ConfigRelativePath))));
        }

        /// <summary>The game config with fixed test balances: a rich player, and a player with 50 cents.</summary>
        public static PoolConfig WithTestPlayers(PoolConfig config = null)
        {
            return (config ?? Load()).With(data => data.TestPlayers = new List<TestPlayerData>
            {
                new TestPlayerData { Id = TestIds.Rich, Name = "Rich", StartingBalanceCents = 1000000000 },
                new TestPlayerData { Id = TestIds.Poor, Name = "Poor", StartingBalanceCents = 50 }
            });
        }
    }

    public static class TestIds
    {
        public const string Rich = "rich";
        public const string Poor = "poor";
        public const string Inner = "Inner";
        public const string Outer = "Outer";
        public const long OneDollar = 100;
    }

    public sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    }

    public sealed class CapturingLog : IEngineLog
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public void Info(string message) { }
        public void Warning(string message) => Warnings.Add(message);
        public void Error(string message) => Errors.Add(message);
    }

    /// <summary>Holds background work until the test releases it, to prove a spin never waits for a build.</summary>
    public sealed class QueuedRunner : IBackgroundRunner
    {
        private readonly Queue<Action> _queue = new Queue<Action>();

        public int Pending => _queue.Count;

        public void Run(Action work) => _queue.Enqueue(work);

        public void RunAll()
        {
            while (_queue.Count > 0) _queue.Dequeue()();
        }
    }

    public static class Services
    {
        public static DealService Open(IPoolStore store, PoolConfig config = null, IBackgroundRunner runner = null,
            IClock clock = null, IEngineLog log = null)
        {
            return new DealService(config ?? TestConfig.WithTestPlayers(), store, runner ?? new InlineBackgroundRunner(),
                clock: clock, log: log);
        }

        /// <summary>Spins and shows the result, like a player who watches every animation to the end.</summary>
        public static SpinRecord SpinAndShow(this DealService service, string mode = TestIds.Inner, long betCents = TestIds.OneDollar,
            string playerId = TestIds.Rich)
        {
            var outcome = service.Spin(playerId, mode, betCents);
            if (!outcome.Accepted) throw new InvalidOperationException("Spin rejected: " + outcome.Rejection + " " + outcome.Message);
            service.MarkResultShown(outcome.Record.SpinId);
            return outcome.Record;
        }

        public static string TempFolder()
        {
            var path = Path.Combine(Path.GetTempPath(), "FinitePrizePoolTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
