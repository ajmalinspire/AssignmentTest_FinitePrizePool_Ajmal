using System.Collections.Generic;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Maths;

namespace FinitePrizePool.Engine.Placement
{
    /// <summary>
    /// Builds the category list in two random steps, so that every §4.4 limit holds by construction:
    ///
    /// 1. Fisher–Yates shuffle the R Regular and B Big labels into a random prize order.
    /// 2. Drop the L losses one at a time into the P + 1 gaps around the prizes (before the first, between two,
    ///    after the last). Each loss goes into a gap chosen uniformly at random among the gaps that still have
    ///    room under all three limits:
    ///      - the gap itself holds at most "max consecutive losses";
    ///      - the losses plus Big slots between two Regular slots stay within "max Regular gap";
    ///      - the losses plus Regular slots between two Big slots stay within "max Big gap".
    ///
    /// Every random choice uses <see cref="ISecureRandom"/>. The limits are never loosened: if the prize order
    /// leaves a group with no room, or the losses cannot all be placed, the attempt returns null and the
    /// builder tries a fresh shuffle. The finished list is still checked by <see cref="PlacementValidator"/>.
    ///
    /// Why not the literal "shuffle the whole list, then check"? With limits at twice the average gap, a
    /// plain shuffle almost always breaks a limit somewhere once a deal has more than a few dozen prizes, so
    /// the larger deals would never build within 1,000 attempts (see the design note and
    /// <see cref="ShuffleAndCheckPlacement"/>).
    /// </summary>
    public sealed class ConstrainedShufflePlacement : IPlacementStrategy
    {
        public string Name => PlacementStrategyNames.ConstrainedShuffle;

        public SlotCategory[] TryGenerate(DealPlan plan, PlacementLimits limits, ISecureRandom random)
        {
            var prizeOrder = ShuffledPrizeOrder(plan, random);
            var gaps = new GapLayout(prizeOrder, limits);
            if (!gaps.HasRoomEverywhere) return null;
            if (!gaps.Fill((int)plan.LosingTickets, random)) return null;
            return gaps.ToSlots((int)plan.PaidSpins);
        }

        private static SlotCategory[] ShuffledPrizeOrder(DealPlan plan, ISecureRandom random)
        {
            var order = new SlotCategory[plan.PrizeTickets];
            for (var i = 0; i < order.Length; i++) order[i] = i < plan.RegularTickets ? SlotCategory.Regular : SlotCategory.Big;
            FisherYates.Shuffle(order, random);
            return order;
        }

        /// <summary>
        /// The P + 1 loss gaps around a fixed prize order, and how much room each has. Gap k sits just before
        /// prize k. Gaps between the same two Regular slots form a "Regular group" (a contiguous range of gaps);
        /// likewise for Big. A loss added to gap k uses room in the gap, its Regular group and its Big group.
        /// </summary>
        private sealed class GapLayout
        {
            private readonly SlotCategory[] _prizeOrder;
            private readonly int[] _gapSize;
            private readonly int[] _gapRoom;
            private readonly int[] _regularGroupOf;
            private readonly int[] _bigGroupOf;
            private readonly List<int> _regularRoom = new List<int>();
            private readonly List<int> _bigRoom = new List<int>();
            private readonly List<int> _regularGroupStart = new List<int>();
            private readonly List<int> _bigGroupStart = new List<int>();

            // Gaps that can still take a loss, with each gap's index in this list (or -1) for O(1) removal.
            private readonly List<int> _open = new List<int>();
            private readonly int[] _openIndex;

            // True when the prize order alone already breaks a gap limit (e.g. too many Regulars between two Bigs).
            private bool _noRoom;

            public bool HasRoomEverywhere => !_noRoom;

            public GapLayout(SlotCategory[] prizeOrder, PlacementLimits limits)
            {
                _prizeOrder = prizeOrder;
                var gapCount = prizeOrder.Length + 1;
                _gapSize = new int[gapCount];
                _gapRoom = new int[gapCount];
                _regularGroupOf = new int[gapCount];
                _bigGroupOf = new int[gapCount];
                _openIndex = new int[gapCount];

                // Walk the prizes once: every Regular closes a Regular group and sits inside the current Big group.
                var bigsInRegularGroup = new List<int> { 0 };
                var regularsInBigGroup = new List<int> { 0 };
                _regularGroupStart.Add(0);
                _bigGroupStart.Add(0);
                for (var k = 0; k < gapCount; k++)
                {
                    _gapRoom[k] = limits.MaxConsecutiveLosses;
                    _regularGroupOf[k] = bigsInRegularGroup.Count - 1;
                    _bigGroupOf[k] = regularsInBigGroup.Count - 1;
                    if (k == prizeOrder.Length) break;

                    if (prizeOrder[k] == SlotCategory.Regular)
                    {
                        regularsInBigGroup[regularsInBigGroup.Count - 1]++;
                        bigsInRegularGroup.Add(0);
                        _regularGroupStart.Add(k + 1);
                    }
                    else
                    {
                        bigsInRegularGroup[bigsInRegularGroup.Count - 1]++;
                        regularsInBigGroup.Add(0);
                        _bigGroupStart.Add(k + 1);
                    }
                }
                _regularGroupStart.Add(gapCount);
                _bigGroupStart.Add(gapCount);

                foreach (var bigs in bigsInRegularGroup) AddRoom(_regularRoom, limits.MaxRegularGap - bigs);
                foreach (var regulars in regularsInBigGroup) AddRoom(_bigRoom, limits.MaxBigGap - regulars);

                for (var k = 0; k < gapCount; k++)
                {
                    _openIndex[k] = -1;
                    if (RoomAt(k) > 0) Open(k);
                }
            }

            private void AddRoom(List<int> rooms, int room)
            {
                if (room < 0) _noRoom = true;
                rooms.Add(room);
            }

            private int RoomAt(int gap)
            {
                if (_noRoom) return 0;
                var room = _gapRoom[gap];
                var regular = _regularRoom[_regularGroupOf[gap]];
                var big = _bigRoom[_bigGroupOf[gap]];
                if (regular < room) room = regular;
                if (big < room) room = big;
                return room;
            }

            public bool Fill(int losses, ISecureRandom random)
            {
                if (_noRoom) return false;
                for (var i = 0; i < losses; i++)
                {
                    if (_open.Count == 0) return false;
                    var gap = _open[random.NextInt(_open.Count)];
                    _gapSize[gap]++;
                    _gapRoom[gap]--;
                    var regularGroup = _regularGroupOf[gap];
                    var bigGroup = _bigGroupOf[gap];
                    _regularRoom[regularGroup]--;
                    _bigRoom[bigGroup]--;

                    if (_gapRoom[gap] == 0) Close(gap);
                    if (_regularRoom[regularGroup] == 0) CloseRange(_regularGroupStart[regularGroup], _regularGroupStart[regularGroup + 1]);
                    if (_bigRoom[bigGroup] == 0) CloseRange(_bigGroupStart[bigGroup], _bigGroupStart[bigGroup + 1]);
                }
                return true;
            }

            public SlotCategory[] ToSlots(int spins)
            {
                var slots = new SlotCategory[spins];
                var position = 0;
                for (var k = 0; k < _gapSize.Length; k++)
                {
                    for (var j = 0; j < _gapSize[k]; j++) slots[position++] = SlotCategory.Loss;
                    if (k < _prizeOrder.Length) slots[position++] = _prizeOrder[k];
                }
                return slots;
            }

            private void Open(int gap)
            {
                _openIndex[gap] = _open.Count;
                _open.Add(gap);
            }

            private void Close(int gap)
            {
                var index = _openIndex[gap];
                if (index < 0) return;
                var last = _open[_open.Count - 1];
                _open[index] = last;
                _openIndex[last] = index;
                _open.RemoveAt(_open.Count - 1);
                _openIndex[gap] = -1;
            }

            private void CloseRange(int fromGap, int toGapExclusive)
            {
                for (var gap = fromGap; gap < toGapExclusive; gap++) Close(gap);
            }
        }
    }
}
