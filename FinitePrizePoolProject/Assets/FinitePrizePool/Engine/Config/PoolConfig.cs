using System;
using System.Collections.Generic;
using System.Linq;

namespace FinitePrizePool.Engine.Config
{
    /// <summary>
    /// The validated, read-only configuration the engine works with. Every deal records the
    /// <see cref="ConfigVersion"/> it was built from, and a config change only affects deals built after it.
    /// </summary>
    public sealed class PoolConfig
    {
        private readonly Dictionary<string, ModeDefinition> _modes;

        public PoolConfigData Data { get; }
        public string ConfigVersion => Data.ConfigVersion;
        public int TargetRtpBasisPoints => Data.TargetRtpBasisPoints;
        public RtpMode RtpMode => Data.RtpMode;
        public int ToleranceBasisPoints => Data.ToleranceBasisPoints;
        public long BonusUnitCents => Data.BonusUnitCents;
        public int MaxGenerationAttempts => Data.MaxGenerationAttempts;
        public PlacementSettings Placement => Data.Placement;
        public IReadOnlyList<long> BetLevelsCents { get; }
        public IReadOnlyList<string> ModeNames { get; }
        public IReadOnlyList<TestPlayerData> TestPlayers => Data.TestPlayers;

        private PoolConfig(PoolConfigData data)
        {
            Data = data;
            BetLevelsCents = data.BetLevelsCents.ToList();
            ModeNames = data.Modes.Select(m => m.Name).ToList();
            _modes = data.Modes.ToDictionary(
                m => m.Name,
                m => new ModeDefinition(m.Name, Bag.FromEntries("Regular", m.RegularBag), Bag.FromEntries("Big", m.BigBag)),
                StringComparer.Ordinal);
        }

        /// <summary>Validates the data and returns the config, or throws <see cref="ConfigException"/> listing every problem.</summary>
        public static PoolConfig From(PoolConfigData data)
        {
            var errors = ConfigValidator.Validate(data);
            if (errors.Count > 0) throw new ConfigException(errors);
            return new PoolConfig(data);
        }

        public ModeDefinition GetMode(string name)
        {
            if (name != null && _modes.TryGetValue(name, out var mode)) return mode;
            throw new ArgumentException("Unknown mode '" + name + "'.");
        }

        public bool HasMode(string name) => name != null && _modes.ContainsKey(name);

        public bool HasBetLevel(long betCents) => BetLevelsCents.Contains(betCents);

        /// <summary>A modified copy for tests and tools (e.g. tighter limits or a new version ID).</summary>
        public PoolConfig With(Action<PoolConfigData> change)
        {
            var copy = ConfigJson.FromJson(ConfigJson.ToJson(Data));
            change(copy);
            return From(copy);
        }
    }

    public sealed class ConfigException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public ConfigException(IReadOnlyList<string> errors)
            : base("Invalid prize pool config:\n- " + string.Join("\n- ", errors))
        {
            Errors = errors;
        }
    }
}
