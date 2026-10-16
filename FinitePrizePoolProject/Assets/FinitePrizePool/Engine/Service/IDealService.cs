using System.Collections.Generic;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Service
{
    /// <summary>
    /// Everything the game client may do. It never exposes tickets: a client sees a spin's result only after
    /// the ticket has been taken, charged and saved. A server version would expose these same calls over the
    /// network, so the game would not change.
    /// </summary>
    public interface IDealService
    {
        IReadOnlyList<string> Modes { get; }
        IReadOnlyList<long> BetLevelsCents { get; }

        IReadOnlyList<PlayerView> GetPlayers();
        PlayerView GetPlayer(string playerId);

        /// <summary>Starts building the deals for a mode/bet if they do not exist yet. Returns at once.</summary>
        void PrepareSlot(string mode, long betCents);

        /// <summary>What the debug panel may show about a mode/bet: deal ID, state, config version, spins played.</summary>
        SlotView GetSlot(string mode, long betCents);

        /// <summary>
        /// One spin: checks the balance, charges the wager, takes the next ticket of the active deal, credits the
        /// award and saves the spin record, all before returning (so before any animation).
        /// </summary>
        SpinOutcome Spin(string playerId, string mode, long betCents);

        /// <summary>The spin whose result was never shown (game closed mid-animation), or null.</summary>
        SpinRecord GetUnshownResult();

        /// <summary>Called when the result has been shown to the player.</summary>
        void MarkResultShown(long spinId);
    }

    /// <summary>Admin and audit actions, kept apart from what the game can call.</summary>
    public interface IDealAdmin
    {
        string ConfigVersion { get; }
        PoolConfig Config { get; }

        IReadOnlyList<SlotView> GetSlots();
        IReadOnlyList<DealStatus> GetActiveDeals();
        IReadOnlyList<FinishedDealRecord> GetFinishedDeals();
        IReadOnlyList<RetireRecord> GetRetireLog();
        IReadOnlyList<SpinRecord> GetSpinLog();

        /// <summary>Retires the active deal of a mode/bet and switches to a freshly verified replacement (§4.7).</summary>
        RetireRecord Retire(string mode, long betCents, string reason);

        /// <summary>
        /// A new config applies to the next deal only: active deals keep their version, and Ready successors
        /// built on the old version are discarded and rebuilt (§4.6).
        /// </summary>
        void ApplyConfig(PoolConfig config);

        /// <summary>Clears a rejected build so the slot tries again.</summary>
        void RetryBuild(string mode, long betCents);
    }
}
