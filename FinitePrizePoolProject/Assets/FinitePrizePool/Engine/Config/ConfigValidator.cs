using System;
using System.Collections.Generic;
using System.Linq;

namespace FinitePrizePool.Engine.Config
{
    /// <summary>Checks a config before the engine uses it. It reports every problem at once, not just the first.</summary>
    public static class ConfigValidator
    {
        public static List<string> Validate(PoolConfigData data)
        {
            var errors = new List<string>();
            if (data == null)
            {
                errors.Add("Config is empty.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(data.ConfigVersion)) errors.Add("configVersion is required.");
            if (data.TargetRtpBasisPoints <= 0 || data.TargetRtpBasisPoints > 10000)
                errors.Add("targetRtpBasisPoints must be between 1 and 10000 (9000 = 90%).");
            if (data.ToleranceBasisPoints < 0 || data.ToleranceBasisPoints >= data.TargetRtpBasisPoints)
                errors.Add("toleranceBasisPoints must be 0 or more and below the target.");
            if (data.BonusUnitCents <= 0) errors.Add("bonusUnitCents must be positive.");
            if (data.MaxGenerationAttempts < 1) errors.Add("maxGenerationAttempts must be at least 1.");

            ValidateBets(data, errors);
            ValidatePlacement(data.Placement, errors);
            ValidatePlayers(data, errors);
            ValidateModes(data, errors);
            return errors;
        }

        private static void ValidateBets(PoolConfigData data, List<string> errors)
        {
            if (data.BetLevelsCents == null || data.BetLevelsCents.Count == 0)
            {
                errors.Add("betLevelsCents must list at least one bet.");
                return;
            }
            if (data.BetLevelsCents.Any(b => b <= 0)) errors.Add("Every bet level must be positive.");
            if (data.BetLevelsCents.Distinct().Count() != data.BetLevelsCents.Count) errors.Add("Bet levels must be distinct.");
        }

        private static void ValidatePlacement(PlacementSettings placement, List<string> errors)
        {
            if (placement == null)
            {
                errors.Add("placement is required.");
                return;
            }
            if (placement.LimitFactor < 1) errors.Add("placement.limitFactor must be at least 1.");
            if (placement.MaxConsecutiveLossesOverride < 0) errors.Add("placement.maxConsecutiveLossesOverride cannot be negative.");
            if (placement.MaxRegularGapOverride < 0) errors.Add("placement.maxRegularGapOverride cannot be negative.");
            if (placement.MaxBigGapOverride < 0) errors.Add("placement.maxBigGapOverride cannot be negative.");
            if (placement.Strategy != PlacementStrategyNames.ConstrainedShuffle && placement.Strategy != PlacementStrategyNames.ShuffleAndCheck)
                errors.Add("placement.strategy must be '" + PlacementStrategyNames.ConstrainedShuffle + "' or '" + PlacementStrategyNames.ShuffleAndCheck + "'.");
        }

        private static void ValidatePlayers(PoolConfigData data, List<string> errors)
        {
            if (data.TestPlayers == null || data.TestPlayers.Count < 2)
            {
                errors.Add("testPlayers must list at least two players (§5).");
                return;
            }
            if (data.TestPlayers.Any(p => string.IsNullOrWhiteSpace(p.Id))) errors.Add("Every test player needs an id.");
            if (data.TestPlayers.Select(p => p.Id).Distinct().Count() != data.TestPlayers.Count) errors.Add("Test player ids must be distinct.");
            if (data.TestPlayers.Any(p => p.StartingBalanceCents < 0)) errors.Add("Starting balances cannot be negative.");
        }

        private static void ValidateModes(PoolConfigData data, List<string> errors)
        {
            if (data.Modes == null || data.Modes.Count == 0)
            {
                errors.Add("modes must list at least one mode.");
                return;
            }
            if (data.Modes.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() != data.Modes.Count)
                errors.Add("Mode names must be distinct.");

            foreach (var mode in data.Modes)
            {
                if (string.IsNullOrWhiteSpace(mode.Name)) errors.Add("Every mode needs a name.");
                ValidateBag(mode.Name + " Regular bag", mode.RegularBag, errors);
                ValidateBag(mode.Name + " Big bag", mode.BigBag, errors);
            }
        }

        private static void ValidateBag(string label, List<BagEntryData> entries, List<string> errors)
        {
            if (entries == null || entries.Count == 0)
            {
                errors.Add(label + " is empty.");
                return;
            }
            foreach (var entry in entries)
            {
                if (entry.Multiplier.HasValue == entry.BonusUnits.HasValue)
                    errors.Add(label + ": each entry needs exactly one of 'multiplier' or 'bonusUnits'.");
                if (entry.Multiplier <= 0) errors.Add(label + ": multipliers must be positive.");
                if (entry.BonusUnits <= 0) errors.Add(label + ": bonus units must be positive.");
                if (entry.Count < 1) errors.Add(label + ": count must be at least 1.");
            }
        }
    }
}
