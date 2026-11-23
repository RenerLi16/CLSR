using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CLSR.Simulation;

namespace CLSR.Offline
{
    /// <summary>
    /// Reads RescueScenarioData .asset files (Unity YAML) into ScenarioSpec, so the
    /// offline tools use exactly the scenarios the game ships. Supports the
    /// subset Unity writes for this class: nested maps, "- " lists, [] and
    /// plain, single- or double-quoted scalars (with \x, \u escapes).
    /// </summary>
    public static class ScenarioAssetLoader
    {
        public static ScenarioSpec Load(string path) => Parse(File.ReadAllText(path));

        public static ScenarioSpec Parse(string text)
        {
            var lines = new List<string>();
            bool inBody = false;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (!inBody)
                {
                    if (raw.StartsWith("MonoBehaviour:", StringComparison.Ordinal)) inBody = true;
                    continue;
                }
                if (raw.Trim().Length == 0) continue;
                lines.Add(raw);
            }

            int index = 0;
            var root = ParseMap(lines, ref index, Indent(lines, 0));
            return ToSpec(root);
        }

        private static int Indent(List<string> lines, int i)
        {
            if (i >= lines.Count) return -1;
            int n = 0;
            while (n < lines[i].Length && lines[i][n] == ' ') n++;
            return n;
        }

        private static Dictionary<string, object> ParseMap(List<string> lines, ref int i, int indent,
            string firstLine = null)
        {
            var map = new Dictionary<string, object>();
            bool first = firstLine != null;
            while (first || (i < lines.Count && Indent(lines, i) == indent && !IsListItem(lines[i], indent)))
            {
                string line = first ? firstLine : lines[i].Substring(indent);
                if (!first) i++;
                first = false;
                int colon = KeyColon(line);
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim();
                string rest = line.Substring(colon + 1).Trim();

                if (rest.Length == 0)
                {
                    int next = Indent(lines, i);
                    if (next == indent && i < lines.Count && IsListItem(lines[i], indent))
                        map[key] = ParseList(lines, ref i, indent);
                    else if (next > indent)
                    {
                        if (IsListItem(lines[i], next)) map[key] = ParseList(lines, ref i, next);
                        else map[key] = ParseMap(lines, ref i, next);
                    }
                    else map[key] = "";
                }
                else if (rest == "[]") map[key] = new List<object>();
                else if (rest == "{}") map[key] = new Dictionary<string, object>();
                else
                {
                    // Continuation lines of a wrapped scalar are indented further and are not keys.
                    var value = new StringBuilder(rest);
                    while (i < lines.Count && Indent(lines, i) > indent && !IsListItem(lines[i], Indent(lines, i)) &&
                           (rest.StartsWith("\"") || rest.StartsWith("'") || KeyColon(lines[i].Trim()) < 0))
                    {
                        value.Append(' ').Append(lines[i].Trim());
                        i++;
                    }
                    map[key] = Scalar(value.ToString());
                }
            }
            return map;
        }

        private static List<object> ParseList(List<string> lines, ref int i, int indent)
        {
            var list = new List<object>();
            while (i < lines.Count && Indent(lines, i) == indent && IsListItem(lines[i], indent))
            {
                string item = lines[i].Substring(indent + 2);
                i++;
                if (KeyColon(item) >= 0) list.Add(ParseMap(lines, ref i, indent + 2, item));
                else list.Add(Scalar(item.Trim()));
            }
            return list;
        }

        private static bool IsListItem(string line, int indent) =>
            line.Length > indent + 1 && line[indent] == '-' && line[indent + 1] == ' ';

        private static int KeyColon(string line)
        {
            if (line.StartsWith("\"") || line.StartsWith("'")) return -1;
            for (int c = 0; c < line.Length; c++)
                if (line[c] == ':' && (c + 1 == line.Length || line[c + 1] == ' ')) return c;
            return -1;
        }

        private static string Scalar(string value)
        {
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return Unescape(value.Substring(1, value.Length - 2));
            return value;
        }

        private static string Unescape(string value)
        {
            var text = new StringBuilder();
            for (int c = 0; c < value.Length; c++)
            {
                if (value[c] != '\\' || c + 1 >= value.Length)
                {
                    text.Append(value[c]);
                    continue;
                }
                char e = value[++c];
                switch (e)
                {
                    case 'n': text.Append('\n'); break;
                    case 't': text.Append('\t'); break;
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case ' ': text.Append(' '); break;
                    case 'x':
                        text.Append((char)int.Parse(value.Substring(c + 1, 2), NumberStyles.HexNumber));
                        c += 2;
                        break;
                    case 'u':
                        text.Append((char)int.Parse(value.Substring(c + 1, 4), NumberStyles.HexNumber));
                        c += 4;
                        break;
                    default: text.Append(e); break;
                }
            }
            return text.ToString();
        }

        private static string Str(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out object value) && value is string text ? text : "";

        private static int Int(Dictionary<string, object> map, string key, int fallback = 0) =>
            int.TryParse(Str(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

        private static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out object value) || !(value is List<object> list)) yield break;
            foreach (object item in list)
                if (item is Dictionary<string, object> entry) yield return entry;
        }

        private static ScenarioSpec ToSpec(Dictionary<string, object> map)
        {
            var nodes = new List<NodeSpec>();
            foreach (var node in Items(map, "nodes"))
                nodes.Add(new NodeSpec(Str(node, "nodeId"), Str(node, "parentNodeId"), Int(node, "depth"),
                    Int(node, "difficulty"), Int(node, "survivorCount"), Int(node, "logisticsReward")));

            var branches = new List<BranchReportSpec>();
            foreach (var branch in Items(map, "branches"))
                branches.Add(new BranchReportSpec(Str(branch, "branchName"), Str(branch, "rootNodeId"),
                    Int(branch, "reportedSurvivors"), Str(branch, "note")));

            var reports = new List<IntelReportSpec>();
            foreach (var report in Items(map, "intelligenceReports"))
            {
                var revisions = new List<EstimateRevisionSpec>();
                foreach (var revision in Items(report, "revisedEstimates"))
                    revisions.Add(new EstimateRevisionSpec(Str(revision, "rootNodeId"), Int(revision, "reportedSurvivors")));
                reports.Add(new IntelReportSpec(Int(report, "afterDay", 2), Str(report, "nodeId"),
                    Int(report, "newDifficulty"), revisions, Str(report, "message")));
            }

            // Field defaults mirror RescueScenarioData's initialisers.
            return new ScenarioSpec(Str(map, "scenarioName"), Int(map, "totalDays", 3),
                Int(map, "totalLogisticsPoints", 9), Int(map, "dailyDeploymentCap", 4),
                Int(map, "maxExpeditionsPerDay", 2), Str(map, "briefing"), nodes, branches, reports);
        }
    }
}
