using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Engine.Service
{
    public sealed class PlayerView
    {
        public string Id;
        public string Name;
        public long BalanceCents;

        /// <summary>How many spins this player has made, across every mode and bet.</summary>
        public long SpinCount;
    }

    /// <summary>
    /// A mode/bet as the game and the debug panel see it. It holds nothing about upcoming tickets or the
    /// prizes left, on purpose.
    /// </summary>
    public sealed class SlotView
    {
        public string Mode;
        public long BetCents;
        public string ActiveDealId;
        public DealState? ActiveState;
        public string ActiveConfigVersion;
        public long SpinsPlayed;
        public string ReadyDealId;
        public string ReadyConfigVersion;
        public bool IsBuilding;
        public string BuildError;

        public bool CanSpin => ActiveDealId != null;
    }

    internal static class ViewMapper
    {
        public static PlayerView ToView(PlayerState player)
        {
            return new PlayerView { Id = player.Id, Name = player.Name, BalanceCents = player.BalanceCents, SpinCount = player.SpinCount };
        }

        public static SlotView ToView(SlotState slot, bool isBuilding)
        {
            return new SlotView
            {
                Mode = slot.Mode,
                BetCents = slot.BetCents,
                ActiveDealId = slot.Active?.DealId,
                ActiveState = slot.Active?.State,
                ActiveConfigVersion = slot.Active?.ConfigVersion,
                SpinsPlayed = slot.Active?.Position ?? 0,
                ReadyDealId = slot.Ready?.DealId,
                ReadyConfigVersion = slot.Ready?.ConfigVersion,
                IsBuilding = isBuilding,
                BuildError = slot.BuildError
            };
        }
    }

    public enum SpinRejection
    {
        None,
        UnknownPlayer,
        UnknownMode,
        UnknownBet,
        DealNotReady,
        InsufficientBalance,
        PreviousResultNotShown,

        /// <summary>The spin could not be saved, so it did not happen: no ticket was used and nothing was charged.</summary>
        SaveFailed
    }

    public sealed class SpinOutcome
    {
        public bool Accepted;
        public SpinRejection Rejection;
        public string Message;
        public SpinRecord Record;

        public static SpinOutcome Reject(SpinRejection rejection, string message)
        {
            return new SpinOutcome { Accepted = false, Rejection = rejection, Message = message };
        }

        public static SpinOutcome Accept(SpinRecord record)
        {
            return new SpinOutcome { Accepted = true, Rejection = SpinRejection.None, Record = record };
        }
    }
}
