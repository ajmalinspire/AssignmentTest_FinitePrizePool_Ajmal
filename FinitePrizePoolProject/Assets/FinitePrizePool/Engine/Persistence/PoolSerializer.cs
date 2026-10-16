using Newtonsoft.Json;

namespace FinitePrizePool.Engine.Persistence
{
    /// <summary>JSON for saved data. Small files are indented for reading; deal files and log lines are compact.</summary>
    public static class PoolSerializer
    {
        private static readonly JsonSerializerSettings Indented = JsonSetup.Settings(indented: true);
        private static readonly JsonSerializerSettings Compact = JsonSetup.Settings(indented: false);

        public static string ToJson<T>(T value, bool indented = true)
        {
            return JsonConvert.SerializeObject(value, indented ? Indented : Compact);
        }

        public static T FromJson<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Indented);
        }

        /// <summary>A deep copy through JSON, used where saved and in-memory objects must not share state.</summary>
        public static T Clone<T>(T value) => FromJson<T>(ToJson(value, indented: false));
    }
}
