using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FinitePrizePool.Engine.Reports
{
    /// <summary>Tiny CSV writer: quotes a field only when it needs it.</summary>
    internal sealed class Csv
    {
        private readonly StringBuilder _text = new StringBuilder();

        public Csv Row(params object[] fields)
        {
            _text.Append(string.Join(",", fields.Select(Field))).Append('\n');
            return this;
        }

        public Csv Rows<T>(IEnumerable<T> items, System.Func<T, object[]> fields)
        {
            foreach (var item in items) Row(fields(item));
            return this;
        }

        public override string ToString() => _text.ToString();

        private static string Field(object value)
        {
            var text = value is System.IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString() ?? "";
            if (text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
