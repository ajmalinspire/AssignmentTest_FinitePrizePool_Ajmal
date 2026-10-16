using System.Globalization;

namespace FinitePrizePool.Engine.Core
{
    /// <summary>
    /// Money is always a whole number of cents in a <c>long</c>. No float or double touches money anywhere.
    /// </summary>
    public static class Money
    {
        public static string Format(long cents)
        {
            var sign = cents < 0 ? "-" : "";
            var abs = cents < 0 ? -cents : cents;
            return string.Format(CultureInfo.InvariantCulture, "{0}${1}.{2:D2}", sign, abs / 100, abs % 100);
        }

        /// <summary>Plain decimal dollars for CSV, e.g. 1.75.</summary>
        public static string ToDollars(long cents)
        {
            return (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
