using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CLSR.Simulation
{
    /// <summary>Protocol timing and structure as currently implemented. Changing these is a protocol change.</summary>
    public static class StudyProtocol
    {
        public const int Players = 3;
        public const int ScenariosPerSession = 2;
        public const int InitialDiscussionSeconds = 60;
        public const int InterventionReadingSeconds = 15;
        public const int FurtherDiscussionSeconds = 45;
        public const string NoAi = "no_ai";
        public const string DirectRecommendation = "direct_recommendation";
        public const string ConstructiveDissent = "constructive_dissent";

        /// <summary>Neutral procedural message for the No-AI condition; carries no strategic content.</summary>
        public const string NeutralMessage =
            "Please continue discussing your team's rescue plan. No additional information or recommendation is provided.";

        public static string ConditionCode(int condition) => condition switch
        {
            1 => NoAi,
            2 => DirectRecommendation,
            3 => ConstructiveDissent,
            _ => "unassigned",
        };
    }

    /// <summary>
    /// Append-only research event log. Events carry an explicit timestamp
    /// supplied by the caller, so the log itself stays deterministic and
    /// testable. It records game events and intervention exposure; free-text
    /// answers and participant identities are deliberately not accepted.
    /// </summary>
    public sealed class ResearchLog
    {
        public const string SchemaVersion = "clsr-events-v1";

        public sealed class Entry
        {
            public int Sequence;
            public double Seconds;
            public string Type;
            public readonly List<KeyValuePair<string, object>> Fields = new List<KeyValuePair<string, object>>();
        }

        private readonly List<Entry> entries = new List<Entry>();

        public ResearchLog(string sessionId)
        {
            SessionId = sessionId ?? "";
        }

        public string SessionId { get; }
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// Adds an event. Field values may be null, string, bool, int, long,
        /// double, or a list of <see cref="Leg"/>. Pairs are given as key, value, key, value...
        /// </summary>
        public Entry Add(double seconds, string type, params object[] pairs)
        {
            if (string.IsNullOrEmpty(type)) throw new ArgumentException("Event type required.", nameof(type));
            if (pairs != null && pairs.Length % 2 != 0)
                throw new ArgumentException("Fields must be key/value pairs.", nameof(pairs));
            var entry = new Entry { Sequence = entries.Count, Seconds = seconds, Type = type };
            if (pairs != null)
            {
                for (int i = 0; i < pairs.Length; i += 2)
                {
                    if (!(pairs[i] is string key) || key.Length == 0)
                        throw new ArgumentException("Field names must be non-empty strings.", nameof(pairs));
                    object value = pairs[i + 1];
                    if (!IsSupported(value))
                        throw new ArgumentException("Unsupported value for " + key + ".", nameof(pairs));
                    entry.Fields.Add(new KeyValuePair<string, object>(key, value));
                }
            }
            entries.Add(entry);
            return entry;
        }

        private static bool IsSupported(object value) =>
            value == null || value is string || value is bool || value is int || value is long ||
            value is double || value is float || value is IReadOnlyList<Leg>;

        public string ToJsonLines()
        {
            var text = new StringBuilder();
            foreach (Entry entry in entries) text.Append(ToJson(entry)).Append('\n');
            return text.ToString();
        }

        public string ToJson(Entry entry)
        {
            var json = new StringBuilder("{");
            json.Append("\"schema\":").Append(SupportSnapshot.Quote(SchemaVersion))
                .Append(",\"session\":").Append(SupportSnapshot.Quote(SessionId))
                .Append(",\"seq\":").Append(entry.Sequence.ToString(CultureInfo.InvariantCulture))
                .Append(",\"t\":").Append(entry.Seconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(",\"type\":").Append(SupportSnapshot.Quote(entry.Type));
            foreach (KeyValuePair<string, object> field in entry.Fields)
                json.Append(',').Append(SupportSnapshot.Quote(field.Key)).Append(':').Append(Value(field.Value));
            return json.Append('}').ToString();
        }

        private static string Value(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string text: return SupportSnapshot.Quote(text);
                case bool flag: return flag ? "true" : "false";
                case int number: return number.ToString(CultureInfo.InvariantCulture);
                case long number: return number.ToString(CultureInfo.InvariantCulture);
                case double number: return number.ToString("R", CultureInfo.InvariantCulture);
                case float number: return ((double)number).ToString("R", CultureInfo.InvariantCulture);
                case IReadOnlyList<Leg> plan:
                    var json = new StringBuilder("[");
                    for (int i = 0; i < plan.Count; i++)
                    {
                        if (i > 0) json.Append(',');
                        json.Append("{\"target\":").Append(SupportSnapshot.Quote(plan[i].Target))
                            .Append(",\"points\":").Append(plan[i].Points.ToString(CultureInfo.InvariantCulture))
                            .Append('}');
                    }
                    return json.Append(']').ToString();
                default: return "null";
            }
        }
    }
}
