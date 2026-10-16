using System;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using UnityEngine;

namespace FinitePrizePool.Game
{
    /// <summary>Sends engine log lines to the Unity console (Debug.Log is safe from background threads).</summary>
    public sealed class UnityEngineLog : IEngineLog
    {
        private const string Prefix = "[FinitePrizePool] ";

        public void Info(string message) => Debug.Log(Prefix + message);
        public void Warning(string message) => Debug.LogWarning(Prefix + message);
        public void Error(string message) => Debug.LogError(Prefix + message);
    }

    /// <summary>Loads the config JSON from Assets/FinitePrizePool/Resources/FinitePrizePoolConfig.json.</summary>
    public static class ConfigAsset
    {
        public const string ResourceName = "FinitePrizePoolConfig";
        public const string AssetPath = "Assets/FinitePrizePool/Resources/" + ResourceName + ".json";

        public static PoolConfig Load()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) throw new InvalidOperationException("Config not found at " + AssetPath + ".");
            return ConfigJson.Load(asset.text);
        }
    }
}
