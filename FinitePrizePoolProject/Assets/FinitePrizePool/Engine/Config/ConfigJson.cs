using FinitePrizePool.Engine.Persistence;
using Newtonsoft.Json;

namespace FinitePrizePool.Engine.Config
{
    /// <summary>Reads and writes the config JSON (camelCase keys, enums as names).</summary>
    public static class ConfigJson
    {
        public static PoolConfigData FromJson(string json)
        {
            var data = JsonConvert.DeserializeObject<PoolConfigData>(json, JsonSetup.Settings(indented: true));
            if (data == null) throw new ConfigException(new[] { "Config JSON is empty." });
            return data;
        }

        public static string ToJson(PoolConfigData data)
        {
            return JsonConvert.SerializeObject(data, JsonSetup.Settings(indented: true));
        }

        /// <summary>Parses and validates in one step.</summary>
        public static PoolConfig Load(string json) => PoolConfig.From(FromJson(json));
    }
}
