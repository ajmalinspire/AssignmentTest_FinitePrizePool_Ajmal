using System.Collections.Generic;

namespace FinitePrizePool.Engine.Placement
{
    /// <summary>What a category list measures against the §4.4 limits.</summary>
    public sealed class PlacementReport
    {
        public int Regular;
        public int Big;
        public int Losses;
        public int MaxLossRun;
        public int MaxRegularGap;
        public int MaxBigGap;
        public bool HasRepeatingInterval;
        public readonly List<string> Violations = new List<string>();

        public bool IsValid => Violations.Count == 0;
    }

    /// <summary>
    /// Checks a finished category list against the limits. It is independent of the strategy that produced
    /// the list, so a bug in a generator cannot hide a broken deal.
    /// </summary>
    public static class PlacementValidator
    {
        public static PlacementReport Validate(IReadOnlyList<SlotCategory> slots, PlacementLimits limits,
            long expectedRegular, long expectedBig, long expectedLosses)
        {
            var report = Measure(slots);

            if (report.Regular != expectedRegular || report.Big != expectedBig || report.Losses != expectedLosses)
                report.Violations.Add("counts are " + report.Regular + " Regular / " + report.Big + " Big / " + report.Losses
                                      + " Loss, expected " + expectedRegular + " / " + expectedBig + " / " + expectedLosses);
            if (report.MaxLossRun > limits.MaxConsecutiveLosses)
                report.Violations.Add(report.MaxLossRun + " losses in a row (limit " + limits.MaxConsecutiveLosses + ")");
            if (report.MaxRegularGap > limits.MaxRegularGap)
                report.Violations.Add("Regular gap of " + report.MaxRegularGap + " (limit " + limits.MaxRegularGap + ")");
            if (report.MaxBigGap > limits.MaxBigGap)
                report.Violations.Add("Big gap of " + report.MaxBigGap + " (limit " + limits.MaxBigGap + ")");
            if (limits.ForbidRepeatingInterval && report.HasRepeatingInterval)
                report.Violations.Add("prize slots follow a fixed repeating interval");

            return report;
        }

        public static PlacementReport Measure(IReadOnlyList<SlotCategory> slots)
        {
            var report = new PlacementReport();
            var prizePositions = new List<int>();
            int lossRun = 0, lastRegular = -1, lastBig = -1;

            for (var i = 0; i < slots.Count; i++)
            {
                switch (slots[i])
                {
                    case SlotCategory.Loss:
                        report.Losses++;
                        lossRun++;
                        if (lossRun > report.MaxLossRun) report.MaxLossRun = lossRun;
                        continue;
                    case SlotCategory.Regular:
                        report.Regular++;
                        report.MaxRegularGap = Max(report.MaxRegularGap, i - lastRegular - 1);
                        lastRegular = i;
                        break;
                    case SlotCategory.Big:
                        report.Big++;
                        report.MaxBigGap = Max(report.MaxBigGap, i - lastBig - 1);
                        lastBig = i;
                        break;
                }
                lossRun = 0;
                prizePositions.Add(i);
            }

            // The end of the deal closes the last gap of each kind.
            report.MaxRegularGap = Max(report.MaxRegularGap, slots.Count - lastRegular - 1);
            report.MaxBigGap = Max(report.MaxBigGap, slots.Count - lastBig - 1);
            report.HasRepeatingInterval = report.Losses > 0 && IntervalsRepeat(prizePositions);
            return report;
        }

        /// <summary>
        /// True when the distances between consecutive prize slots repeat with some period q (the same
        /// interval pattern at least twice in a row, covering the whole deal). A constant spacing is q = 1.
        /// </summary>
        public static bool IntervalsRepeat(IReadOnlyList<int> prizePositions)
        {
            var count = prizePositions.Count - 1;
            if (count < 2) return false;

            var intervals = new int[count];
            for (var i = 0; i < count; i++) intervals[i] = prizePositions[i + 1] - prizePositions[i];

            for (var period = 1; period <= count / 2; period++)
            {
                var repeats = true;
                for (var i = 0; i + period < count; i++)
                {
                    if (intervals[i] == intervals[i + period]) continue;
                    repeats = false;
                    break;
                }
                if (repeats) return true;
            }
            return false;
        }

        private static int Max(int a, int b) => a > b ? a : b;
    }
}
