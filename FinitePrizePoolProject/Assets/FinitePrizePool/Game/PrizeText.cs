using System.Globalization;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Game
{
    /// <summary>Words for a spin result: the win amount and the prize type (multiplier, bonus or loss).</summary>
    public static class PrizeText
    {
        public static string Headline(SpinRecord spin)
        {
            switch (spin.PrizeType)
            {
                case PrizeKind.Multiplier: return "WIN " + Money.Format(spin.AwardCents);
                case PrizeKind.Bonus: return "BONUS " + Money.Format(spin.AwardCents);
                default: return "No win";
            }
        }

        public static string Detail(SpinRecord spin)
        {
            switch (spin.PrizeType)
            {
                case PrizeKind.Multiplier:
                    return "Multiplier " + spin.Multiplier.ToString("0.##", CultureInfo.InvariantCulture) + "x on a " + Money.Format(spin.BetCents) + " bet";
                case PrizeKind.Bonus:
                    return "Fixed bonus of " + spin.BonusUnits + " unit" + (spin.BonusUnits == 1 ? "" : "s");
                default:
                    return "Loss";
            }
        }

        /// <summary>
        /// "Player 1's spin 3  |  Inner $1.00  |  spin ID 12". The player's spin number counts only their spins;
        /// the spin ID is unique on the device, shared by every player, mode and bet.
        /// </summary>
        public static string Footer(SpinRecord spin, string playerName)
        {
            var who = spin.PlayerSpinNumber > 0 ? playerName + "'s spin " + spin.PlayerSpinNumber : playerName;
            return who + "  |  " + spin.Mode + " " + Money.Format(spin.BetCents) + "  |  spin ID " + spin.SpinId;
        }

        /// <summary>"1 spin", "3 spins".</summary>
        public static string SpinCount(long count) => count + (count == 1 ? " spin" : " spins");
    }
}
