using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace FinitePrizePool.Engine.Persistence
{
    /// <summary>One place for the JSON settings, so config, saves and reports all use the same format.</summary>
    public static class JsonSetup
    {
        public static JsonSerializerSettings Settings(bool indented)
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                FloatParseHandling = FloatParseHandling.Decimal,
                Formatting = indented ? Formatting.Indented : Formatting.None
            };
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }
    }
}
