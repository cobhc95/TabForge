using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TabForge;

/// <summary>
/// ArchitectureBudget.json: reading, comparing (the ratchet) and recording. Rule: a measured value above its budget fails; below it, a normal
/// run logs how to lower the budget and a strict run (TABFORGE_ARCH_STRICT=1 or "strict-lower", the merge gate) fails, so the file always
/// states exactly what the code is. A budget is raised only by an explicit edit with a reason in the commit message.
/// </summary>
public static partial class SelfTest
{
    private static readonly JsonSerializerOptions ArchJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string ArchQuote(string s) => JsonSerializer.Serialize(s, ArchJson);

    private sealed class ArchBudget
    {
        private readonly JsonElement _root;
        private ArchBudget(JsonElement root) { _root = root; }

        public static ArchBudget Load(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            return new ArchBudget(doc.RootElement.Clone());
        }

        public long? Number(string key) => _root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

        public bool Has(string key) => _root.TryGetProperty(key, out _);

        public HashSet<string> Set(string key)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (_root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var item in v.EnumerateArray()) set.Add(item.GetString() ?? "");
            return set;
        }

        public Dictionary<string, long> Map(string key)
        {
            var map = new Dictionary<string, long>(StringComparer.Ordinal);
            if (_root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object)
                foreach (var p in v.EnumerateObject()) map[p.Name] = p.Value.GetInt64();
            return map;
        }

        public Dictionary<string, ArchClassSpec> Classes()
        {
            var result = new Dictionary<string, ArchClassSpec>(StringComparer.Ordinal);
            if (!_root.TryGetProperty("classes", out var v) || v.ValueKind != JsonValueKind.Object) return result;
            foreach (var p in v.EnumerateObject())
            {
                var spec = new ArchClassSpec { Type = p.Name };
                foreach (var field in p.Value.EnumerateObject())
                {
                    if (field.Name == "files") foreach (var f in field.Value.EnumerateArray()) spec.Files.Add(f.GetString() ?? "");
                    else if (field.Name == "ceiling") spec.Ceiling = field.Value.GetBoolean();
                    else if (field.Value.ValueKind == JsonValueKind.Number) spec.Numbers[field.Name] = field.Value.GetInt64();
                }
                result[p.Name] = spec;
            }
            return result;
        }
    }

    // ---------- the ratchet ----------

    private sealed class ArchRatchet
    {
        private readonly bool _strictLower;
        public ArchRatchet(bool strictLower) { _strictLower = strictLower; }

        public void Sets(string id, string what, IReadOnlyCollection<string> actual, HashSet<string> budget, string key)
        {
            var rises = actual.Where(a => !budget.Contains(a)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var falls = budget.Where(b => !actual.Contains(b)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Report(id, what, key, rises.Select(r => $"{r} is new (not in the budget list)").ToList(),
                falls.Select(f => (f, ArchQuote(f) + ",")).ToList(), actual.Count, budget.Count);
        }

        public void Maps(string id, string what, IReadOnlyDictionary<string, long> actual, Dictionary<string, long> budget, string key,
            Func<string, bool>? exemptFromFall = null, Func<string, long, string>? line = null)
        {
            line ??= (k, v) => $"{ArchQuote(k)}: {v}";
            var rises = new List<string>();
            var falls = new List<(string Key, string Line)>();
            foreach (var (k, v) in actual.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var b = budget.GetValueOrDefault(k);
                if (v > b) rises.Add($"{k}: {v} > budget {b}");
                else if (v < b && exemptFromFall?.Invoke(k) != true) falls.Add((k, line(k, v)));
            }
            foreach (var (k, b) in budget.OrderBy(x => x.Key, StringComparer.Ordinal))
                if (!actual.ContainsKey(k) && b > 0 && exemptFromFall?.Invoke(k) != true) falls.Add((k, $"remove {line(k, b)}"));
            Report(id, what, key, rises, falls, actual.Values.Sum(), budget.Values.Sum());
        }

        public void Scalar(string id, string what, long actual, long? budget, string key)
        {
            var rises = new List<string>();
            var falls = new List<(string, string)>();
            if (budget is null) rises.Add($"{key} has no budget (measured {actual}): record it");
            else if (actual > budget) rises.Add($"{actual} > budget {budget}");
            else if (actual < budget) falls.Add((key, $"{ArchQuote(key)}: {actual}"));
            Report(id, what, key, rises, falls, actual, budget ?? 0);
        }

        private void Report(string id, string what, string key, List<string> rises, List<(string Key, string Line)> falls, long actual, long budget)
        {
            Check($"{id} {what}: nothing above the budget in {key} ({actual} measured, {budget} budgeted)", rises.Count == 0,
                string.Join("; ", rises.Take(8)) + (rises.Count > 8 ? $"; +{rises.Count - 8} more" : "") + "  (raising a budget needs an explicit edit with a reason)");
            var hint = "lower the budget: " + string.Join(" | ", falls.Take(8).Select(f => f.Line)) + (falls.Count > 8 ? $" | +{falls.Count - 8} more" : "");
            Check($"{id} {what}: {key} is as tight as the code{(_strictLower ? " (strict-lower)" : "")}", !_strictLower || falls.Count == 0, hint);
            if (!_strictLower && falls.Count > 0) Log.Add($"  info  {id} {key}: {hint}");
        }
    }

    // ---------- recording ----------

    private static string ArchRecord(ArchMeasured m, Dictionary<string, ArchClassSpec> specs, ArchBudget old)
    {
        var sb = new StringBuilder();
        var sections = new List<string>();

        string Scalar(string key, long? v) => $"  {ArchQuote(key)}: {(v.HasValue ? v.Value.ToString() : "null")}";
        string Set(string key, IEnumerable<string> items)
        {
            var lines = items.Select(i => "    " + ArchQuote(i)).ToList();
            return lines.Count == 0 ? $"  {ArchQuote(key)}: []" : $"  {ArchQuote(key)}: [\n{string.Join(",\n", lines)}\n  ]";
        }
        string Map(string key, IEnumerable<KeyValuePair<string, long>> items)
        {
            var lines = items.Select(i => $"    {ArchQuote(i.Key)}: {i.Value}").ToList();
            return lines.Count == 0 ? $"  {ArchQuote(key)}: {{}}" : $"  {ArchQuote(key)}: {{\n{string.Join(",\n", lines)}\n  }}";
        }

        sections.Add(Scalar("version", 1));
        sections.Add(Set("layerExceptions", m.LayerViolators.Keys));
        sections.Add(Set("delegateHostFields", m.DelegateHostFields));
        sections.Add(Set("wpfExceptions", m.WpfExceptions));

        var classLines = new List<string>();
        foreach (var (type, spec) in specs.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            long Value(string metric)
            {
                var v = m.ClassValues.GetValueOrDefault(type + "|" + metric);
                if (spec.Ceiling && metric == "lines") return (v + 999) / 1000 * 1000;
                if (spec.Ceiling && metric == "fileCount") return (v + 9) / 10 * 10;
                return v;
            }
            var parts = new List<string> { $"\"files\": [{string.Join(", ", spec.Files.Select(ArchQuote))}]" };
            if (spec.Ceiling) parts.Add("\"ceiling\": true");
            parts.Add($"\"lines\": {Value("lines")}");
            parts.Add($"\"fileCount\": {Value("fileCount")}");
            if (!spec.Ceiling)
            {
                parts.Add($"\"fields\": {Value("fields")}");
                parts.Add($"\"methods\": {Value("methods")}");
                if (m.IlMeasured) parts.Add($"\"il\": {Value("il")}");
                else if (spec.Numbers.TryGetValue("il", out var oldIl)) parts.Add($"\"il\": {oldIl}");
            }
            classLines.Add($"    {ArchQuote(type)}: {{{string.Join(", ", parts)}}}");
        }
        sections.Add($"  \"classes\": {{\n{string.Join(",\n", classLines)}\n  }}");
        sections.Add(Scalar("maxFileLines", old.Number("maxFileLines") ?? 1000));
        sections.Add(Scalar("maxInstanceFields", old.Number("maxInstanceFields") ?? 40));
        sections.Add(Map("fileLineExceptions", m.FilesOverLineCap));
        sections.Add(Map("fieldCapExceptions", m.TypesOverFieldCap));
        sections.Add(Scalar("methodIlThreshold", m.MethodIlThreshold));
        sections.Add(Map("largeMethods", m.IlMeasured ? m.LargeMethods : old.Map("largeMethods")));
        sections.Add(Set("staticsAllow", m.MutableStatics));
        sections.Add(Scalar("staticReadonlyCollections", m.StaticReadonlyCollections));
        sections.Add(Map("historyComments", m.HistoryComments));
        sections.Add(Scalar("probeAccessMembers", m.ProbeAccessMembers ?? old.Number("probeAccessMembers")));
        sections.Add(Set("diagnosticsReferences", m.DiagnosticsReferences));
        sections.Add(Map("dirtyOutsidePathway", m.DirtyOutsidePathway));
        sections.Add(Map("undoOutsideDocuments", m.UndoOutsideDocuments));
        sections.Add(Set("windowDocumentFields", m.WindowDocumentFields));
        sections.Add(Map("asyncActiveReads", m.AsyncActiveReads));
        sections.Add(Set("ambientStatics", m.AmbientStatics));
        sections.Add(Set("namingExceptions", m.NamingViolations));
        sections.Add(Set("ownershipHeaderExceptions", old.Set("ownershipHeaderExceptions").OrderBy(x => x, StringComparer.Ordinal)));
        sections.Add("  \"perf\": {}");
        sb.Append("{\n").Append(string.Join(",\n", sections)).Append("\n}\n");
        return sb.ToString();
    }
}
