using System;
using System.Threading.Tasks;

namespace FinitePrizePool.Engine.Core
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>Engine logging, so the engine never depends on UnityEngine.Debug.</summary>
    public interface IEngineLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    public sealed class NullEngineLog : IEngineLog
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    /// <summary>Runs deal building off the caller's thread, so a spin never waits for a deal to be built.</summary>
    public interface IBackgroundRunner
    {
        void Run(Action work);
    }

    /// <summary>Thread-pool runner used by the game.</summary>
    public sealed class TaskBackgroundRunner : IBackgroundRunner
    {
        private readonly IEngineLog _log;

        public TaskBackgroundRunner(IEngineLog log)
        {
            _log = log ?? new NullEngineLog();
        }

        public void Run(Action work)
        {
            Task.Run(() =>
            {
                try { work(); }
                catch (Exception e) { _log.Error("Background deal build failed: " + e); }
            });
        }
    }

    /// <summary>Runs the work immediately on the caller's thread. Used by tests and the simulator.</summary>
    public sealed class InlineBackgroundRunner : IBackgroundRunner
    {
        public void Run(Action work) => work();
    }
}
