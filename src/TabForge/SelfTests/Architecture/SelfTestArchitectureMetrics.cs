using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace TabForge;

/// <summary>
/// The measurements behind the architecture guards (reflection over the compiled assemblies, plus source scans of the checkout).
/// <see cref="ArchMeasure"/> returns every value; the guards in SelfTestArchitecture.cs compare them with ArchitectureBudget.json.
/// </summary>
public static partial class SelfTest
{
    private const BindingFlags ArchAllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] ArchLowerLayers =
        { "TabForge.Models", "TabForge.Services", "TabForge.Plugins", "TabForge.Presets", "TabForge.Rendering", "TabForge.Playback", "TabForge.Audio", "TabForge.Documents" };
    private static readonly string[] ArchUpperLayers = { "TabForge.Views", "TabForge.Shell", "TabForge.Visualization" };
    private static readonly string[] ArchSourceProjects = { "src/TabForge", "src/TabForge.AudioEngine", "src/TabForge.Audio.Contracts", "tests/full-suite" };   // the full suite is measured from its source in every build

    private sealed class ArchClassSpec
    {
        public string Type = "";
        public List<string> Files = new();
        public bool Ceiling;
        public Dictionary<string, long> Numbers = new();
    }

    /// <summary>Every measured value. Sorted collections keep the recorded budget file stable.</summary>
    private sealed class ArchMeasured
    {
        public bool IlMeasured;
        public SortedDictionary<string, string> LayerViolators = new(StringComparer.Ordinal);
        public SortedSet<string> DelegateHostFields = new(StringComparer.Ordinal);
        public SortedSet<string> WpfExceptions = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> ClassValues = new(StringComparer.Ordinal);   // "Type|metric" -> value
        public SortedDictionary<string, long> FilesOverLineCap = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> TypesOverFieldCap = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> LargeMethods = new(StringComparer.Ordinal);
        public long MethodIlThreshold;
        public SortedSet<string> MutableStatics = new(StringComparer.Ordinal);
        public long StaticReadonlyCollections;
        public SortedDictionary<string, long> HistoryComments = new(StringComparer.Ordinal);
        public long? ProbeAccessMembers;
        public SortedSet<string> DiagnosticsReferences = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> DirtyOutsidePathway = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> UndoOutsideDocuments = new(StringComparer.Ordinal);
        public SortedSet<string> WindowDocumentFields = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> AsyncActiveReads = new(StringComparer.Ordinal);
        public SortedSet<string> AmbientStatics = new(StringComparer.Ordinal);
        public SortedSet<string> NamingViolations = new(StringComparer.Ordinal);
        public List<long> ThresholdProfile = new();
    }

    // ---------- helpers ----------

    private static bool ArchIsGenerated(Type t)
    {
        for (var x = t; x is not null; x = x.DeclaringType)
            if (x.Name.StartsWith('<') || x.IsDefined(typeof(CompilerGeneratedAttribute), false)) return true;
        return false;
    }

    private static string ArchTypeName(Type t) => (t.FullName ?? t.Name).Replace('+', '.');

    /// <summary>The nearest enclosing type that is not compiler-generated (closure and state-machine classes belong to their user type).</summary>
    private static Type ArchOwner(Type t)
    {
        var x = t;
        while (x.DeclaringType is { } d && (x.Name.StartsWith('<') || x.IsDefined(typeof(CompilerGeneratedAttribute), false))) x = d;
        return x;
    }

    private static bool ArchInNamespace(Type t, string ns)
    {
        var n = t.Namespace ?? "";
        return n == ns || n.StartsWith(ns + ".", StringComparison.Ordinal);
    }

    private static bool ArchIsSelfTest(Type t) => OutermostType(t).FullName == "TabForge.SelfTest";

    /// <summary>The name a method is budgeted under: closures, local functions and async bodies belong to the method that declares them.</summary>
    private static string? ArchMethodName(Type declaring, MethodBase m, bool declaringGenerated, out bool regular)
    {
        regular = false;
        if (m.Name.StartsWith('<'))
        {
            var end = m.Name.IndexOf('>');
            return end > 1 ? m.Name[1..end] : null;
        }
        if (declaringGenerated)
        {
            if (m.Name != "MoveNext") return null;
            var match = Regex.Match(declaring.Name, @"^<(.+)>d__\d+");
            return match.Success ? match.Groups[1].Value : null;
        }
        if (m.IsDefined(typeof(CompilerGeneratedAttribute), false)) return null;
        regular = true;
        return m.Name;
    }

    private static IEnumerable<Assembly> ArchAssemblies()
    {
        yield return typeof(SelfTest).Assembly;
        foreach (var name in new[] { "TabForge.AudioEngine", "TabForge.Audio.Contracts" })
        {
            Assembly? a = null;
            try { a = Assembly.Load(name); } catch (Exception) { }
            if (a is not null) yield return a;
        }
    }

    private static IEnumerable<Type> ArchTypes(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        return types;
    }

    private static string ArchParameterlessKey(Type owner, string name) => ArchTypeName(owner) + "." + name;

    // ---------- source helpers ----------

    private static List<string> ArchSourceFiles(string root)
    {
        var list = new List<string>();
        foreach (var project in ArchSourceProjects)
        {
            var dir = Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                var inner = rel[(project.Length + 1)..];
                if (inner.StartsWith("obj/", StringComparison.Ordinal) || inner.StartsWith("bin/", StringComparison.Ordinal) ||
                    inner.Contains("/obj/", StringComparison.Ordinal) || inner.Contains("/bin/", StringComparison.Ordinal)) continue;
                list.Add(rel);
            }
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static Regex ArchGlob(string pattern)
    {
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*') { sb.Append(".*"); i++; }
                else sb.Append("[^/]*");
            }
            else sb.Append(Regex.Escape(pattern[i].ToString()));
        }
        return new Regex(sb.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    private static int ArchLineCount(string path)
    {
        var n = 0;
        using var reader = new StreamReader(path);
        while (reader.ReadLine() is not null) n++;
        return n;
    }

    /// <summary>
    /// Blanks strings, character literals, comments and preprocessor lines (newlines kept, so line numbers hold) and returns the comment
    /// lines separately. A line scanner, not a compiler: good enough for pattern counts.
    /// </summary>
    private static (string Code, List<(int Line, string Text)> Comments) ArchStrip(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var comments = new List<(int, string)>();
        int i = 0, n = text.Length, line = 1;
        void Blank(string s) { foreach (var c in s) sb.Append(c == '\n' ? '\n' : ' '); }
        while (i < n)
        {
            var c = text[i];
            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                var j = text.IndexOf('\n', i); if (j < 0) j = n;
                comments.Add((line, text[i..j].TrimEnd('\r')));
                sb.Append(' ', j - i); i = j; continue;
            }
            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                var j = text.IndexOf("*/", i + 2, StringComparison.Ordinal); j = j < 0 ? n : j + 2;
                var seg = text[i..j];
                var k = 0;
                foreach (var part in seg.Split('\n')) comments.Add((line + k++, part.TrimEnd('\r')));
                line += seg.Count(ch => ch == '\n'); Blank(seg); i = j; continue;
            }
            if (c == '\n') { line++; sb.Append(c); i++; continue; }
            if (c == '\'')
            {
                var j = i + 1;
                while (j < n && text[j] != '\'') j += text[j] == '\\' ? 2 : 1;
                sb.Append(' ', Math.Min(j + 1, n) - i); i = Math.Min(j + 1, n); continue;
            }
            if (c is '$' or '@' or '"')
            {
                var j = i; var verbatim = false;
                while (j < n && text[j] is '$' or '@') { verbatim |= text[j] == '@'; j++; }
                if (j < n && text[j] == '"')
                {
                    var q = 0;
                    while (j + q < n && text[j + q] == '"') q++;
                    int end;
                    if (q >= 3) { end = text.IndexOf(new string('"', q), j + q, StringComparison.Ordinal); end = end < 0 ? n : end + q; }
                    else if (verbatim)
                    {
                        end = j + 1;
                        while (end < n)
                        {
                            if (text[end] == '"') { if (end + 1 < n && text[end + 1] == '"') { end += 2; continue; } break; }
                            end++;
                        }
                        end++;
                    }
                    else
                    {
                        end = j + 1;
                        while (end < n && text[end] != '"' && text[end] != '\n') end += text[end] == '\\' ? 2 : 1;
                        end++;
                    }
                    end = Math.Min(end, n);
                    var seg = text[i..end]; line += seg.Count(ch => ch == '\n'); Blank(seg); i = end; continue;
                }
            }
            sb.Append(c); i++;
        }
        var code = Regex.Replace(sb.ToString(), @"(?m)^[ \t]*#.*$", m => new string(' ', m.Length));
        return (code, comments);
    }

    private static readonly Regex[] ArchHistoryPatterns =
    {
        new(@"\b20\d\d-\d\d(-\d\d)?\b", RegexOptions.CultureInvariant),
        new(@"\b(Audit ?\d+|A\d-\d+|[A-H]-\d\d|R\d\b|H-\d|P\d-\d+)", RegexOptions.CultureInvariant),
        new(@"\b(Atlas|Nova|Iris|Remy|Sage|Kestrel|Hale|Quill|Warden|Lyra|Vega|Echo|Juno|Astra|Theo|Kai|Ivy|Vera|Pixel|Lumen|owner (request|decision|asked|wants|wanted|rule|answer)|reviewer|the review|the user (asked|reported|wanted))\b", RegexOptions.CultureInvariant),
        new(@"\b(night ?run|night \d|beta\.?\d|0\.[3-6]\.\d|regression from|this fix|the fix|was fixed|fixed in)\b", RegexOptions.CultureInvariant),
    };

    private static readonly Regex ArchActiveRead = new(@"(?<![\w.])(Doc|_project|_undo|_timeline|_loop\w*|_playhead\w*|Playback)\b(?!\s*\()|\bEditor\.(Track|Project|SelectedTrackIndex)\b|_documents\.Active\b", RegexOptions.CultureInvariant);
    private static readonly Regex ArchAsyncMethod = new(@"\basync\s+[\w<>?,. ]+\s+(\w+)\s*\([^)]*\)\s*\{", RegexOptions.CultureInvariant);
    private static readonly Regex ArchAsyncLambda = new(@"\basync\s*(\([^)]*\)|\w+)\s*=>\s*\{", RegexOptions.CultureInvariant);

    private static string ArchBlockAfter(string code, int openBraceEnd)
    {
        var i = openBraceEnd; var depth = 1;
        while (depth > 0 && i < code.Length) { depth += code[i] == '{' ? 1 : code[i] == '}' ? -1 : 0; i++; }
        return code[openBraceEnd..i];
    }

    // ---------- the measurement ----------

    private static ArchMeasured ArchMeasure(string? root, Dictionary<string, ArchClassSpec> classSpecs, ArchBudget budget)
    {
        var m = new ArchMeasured();
        var maxFileLines = (int)(budget.Number("maxFileLines") ?? 1000);
        var maxFields = (int)(budget.Number("maxInstanceFields") ?? 40);
        m.MethodIlThreshold = budget.Number("methodIlThreshold") ?? 3000;
        var main = typeof(SelfTest).Assembly;
        m.IlMeasured = main.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

        ArchMeasureReflection(m, classSpecs, maxFields);
        ArchMeasureIl(m, main);
        if (root is not null) ArchMeasureSource(m, root, classSpecs, maxFileLines);
        return m;
    }

    private static void ArchMeasureReflection(ArchMeasured m, Dictionary<string, ArchClassSpec> classSpecs, int maxFields)
    {
        var listedTypes = new HashSet<string>(classSpecs.Keys, StringComparer.Ordinal);
        var methodIl = new Dictionary<string, long>(StringComparer.Ordinal);
        var ownerIl = new Dictionary<string, long>(StringComparer.Ordinal);
        var ownerMethods = new Dictionary<string, long>(StringComparer.Ordinal);
        var ownerFields = new Dictionary<string, long>(StringComparer.Ordinal);
        var staticReadonlyCollections = 0L;
        var ambientTypes = new[] { typeof(Models.SongProject), typeof(Documents.DocumentSession), typeof(Models.TrackModel), typeof(Documents.UndoController) };

        foreach (var assembly in ArchAssemblies())
        {
            foreach (var type in ArchTypes(assembly))
            {
                var generated = ArchIsGenerated(type);
                var owner = ArchOwner(type);
                var selfTest = ArchIsSelfTest(type);
                var ownerName = ArchTypeName(owner);

                // methods: IL bytes and counts, attributed to the declaring user type
                foreach (var method in type.GetMethods(ArchAllMembers).Cast<MethodBase>().Concat(type.GetConstructors(ArchAllMembers)))
                {
                    var name = ArchMethodName(type, method, generated, out var regular);
                    if (name is null) continue;
                    long il = 0;
                    try { il = method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0; } catch (Exception) { }
                    ownerIl[ownerName] = ownerIl.GetValueOrDefault(ownerName) + il;
                    if (regular) ownerMethods[ownerName] = ownerMethods.GetValueOrDefault(ownerName) + 1;
                    if (!selfTest)
                    {
                        var key = ownerName + "." + name;
                        methodIl[key] = methodIl.GetValueOrDefault(key) + il;
                    }
                }
                if (generated) continue;

                // G13: naming suffixes (Host is an interface named I...Host; Controller, Flow and Service names are classes)
                var simple = type.Name;
                var bareName = Regex.IsMatch(simple, "^I[A-Z]") ? simple[1..] : simple;
                if (type.IsInterface ? (Regex.IsMatch(bareName, "(Controller|Flow|Service)$") || (simple.EndsWith("Host", StringComparison.Ordinal) && !Regex.IsMatch(simple, "^I[A-Z]")))
                                     : Regex.IsMatch(simple, @"^I[A-Z]\w*Host$"))
                    m.NamingViolations.Add(ArchTypeName(type));

                // fields: instance count for the caps, mutable statics, mutable static collections
                var fields = type.GetFields(ArchAllMembers);
                var instanceFields = fields.Count(f => !f.IsStatic && !f.IsDefined(typeof(CompilerGeneratedAttribute), false));
                ownerFields[ownerName] = instanceFields;
                if (!selfTest && !listedTypes.Contains(ownerName) && instanceFields > maxFields) m.TypesOverFieldCap[ownerName] = instanceFields;

                if (!selfTest)
                {
                    foreach (var f in fields.Where(f => f.IsStatic && !f.IsLiteral))
                    {
                        var generatedField = f.IsDefined(typeof(CompilerGeneratedAttribute), false);
                        if (!f.IsInitOnly)
                        {
                            var label = f.Name;
                            if (generatedField)
                            {
                                var bm = Regex.Match(f.Name, @"^<(.+)>k__BackingField$");
                                if (!bm.Success) continue;
                                label = bm.Groups[1].Value;
                            }
                            m.MutableStatics.Add(ArchTypeName(type) + "." + label);
                        }
                        else if (!generatedField && ArchIsMutableCollection(f.FieldType)) staticReadonlyCollections++;
                    }
                    foreach (var e in type.GetEvents(ArchAllMembers))
                        if ((e.AddMethod ?? e.RemoveMethod)?.IsStatic == true) m.MutableStatics.Add(ArchTypeName(type) + "." + e.Name);

                    // G12: ambient "current" providers and static references to song objects
                    foreach (var f in fields.Where(f => f.IsStatic && !f.IsLiteral && !f.IsDefined(typeof(CompilerGeneratedAttribute), false)))
                        if (ArchIsAmbientType(f.FieldType, ambientTypes) || ArchAmbientName(f.Name)) m.AmbientStatics.Add(ArchTypeName(type) + "." + f.Name);
                    foreach (var p in type.GetProperties(ArchAllMembers))
                        if ((p.GetMethod ?? p.SetMethod)?.IsStatic == true && (ArchIsAmbientType(p.PropertyType, ambientTypes) || ArchAmbientName(p.Name)))
                            m.AmbientStatics.Add(ArchTypeName(type) + "." + p.Name);
                }

                // G10: per-document state held in window and controller fields
                var windowLike = type.FullName == "TabForge.MainWindow" || ArchInNamespace(type, "TabForge.Controllers") ||
                                 (ArchInNamespace(type, "TabForge.Views") && typeof(System.Windows.Window).IsAssignableFrom(type));
                if (windowLike)
                    foreach (var f in fields.Where(f => !f.IsStatic && !f.IsDefined(typeof(CompilerGeneratedAttribute), false)))
                    {
                        var ft = Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType;
                        if (ft == typeof(Documents.UndoSnapshot) || ft == typeof(Documents.UndoController.UndoTransaction) || ft == typeof(Models.SongProject) ||
                            ft == typeof(Documents.DocumentSession) || ft == typeof(Playback.ScoreTimeline) || ft == typeof(Models.TrackModel))
                            m.WindowDocumentFields.Add(ArchTypeName(type) + "." + f.Name);
                    }
            }
        }
        m.StaticReadonlyCollections = staticReadonlyCollections;

        foreach (var (key, il) in methodIl)
            if (il >= m.MethodIlThreshold) m.LargeMethods[key] = il;
        foreach (var t in new long[] { 1500, 2000, 2500, 3000, 3500, 4000, 5000 }) m.ThresholdProfile.Add(methodIl.Values.Count(v => v >= t));

        foreach (var (typeName, spec) in classSpecs)
        {
            var type = typeof(SelfTest).Assembly.GetType(typeName);
            if (type is null) continue;
            m.ClassValues[typeName + "|fields"] = ownerFields.GetValueOrDefault(typeName);
            m.ClassValues[typeName + "|methods"] = ownerMethods.GetValueOrDefault(typeName);
            if (m.IlMeasured) m.ClassValues[typeName + "|il"] = ownerIl.GetValueOrDefault(typeName);
        }
    }

    private static bool ArchAmbientName(string name) => name is "Current" or "Instance" or "Shared";

    private static bool ArchIsAmbientType(Type t, Type[] ambient)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsEnum) return false;
        return ambient.Contains(t) || (t.Name.EndsWith("State", StringComparison.Ordinal) && !t.IsInterface && t.Namespace?.StartsWith("TabForge", StringComparison.Ordinal) == true);
    }

    private static bool ArchIsMutableCollection(Type t)
    {
        if (t.IsArray || t == typeof(string)) return false;
        var ns = t.Namespace ?? "";
        if (ns.Contains("Immutable", StringComparison.Ordinal) || ns.Contains("Frozen", StringComparison.Ordinal) || t.Name.StartsWith("ReadOnly", StringComparison.Ordinal)) return false;
        if (t.IsInterface) return t.IsGenericType ? t.GetGenericTypeDefinition() == typeof(ICollection<>) || t.GetGenericTypeDefinition() == typeof(IList<>) || t.GetGenericTypeDefinition() == typeof(IDictionary<,>) || t.GetGenericTypeDefinition() == typeof(ISet<>) : t == typeof(System.Collections.ICollection) || t == typeof(System.Collections.IList) || t == typeof(System.Collections.IDictionary);
        return typeof(System.Collections.ICollection).IsAssignableFrom(t) ||
               t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));
    }

    /// <summary>The IL walk: layering (G1), engine / diagnostics pins (G7), undo capture sites (G9).</summary>
    private static void ArchMeasureIl(ArchMeasured m, Assembly main)
    {
        var unresolved = 0;
        var undoMethods = new HashSet<string>(StringComparer.Ordinal) { "Capture", "BeginTransaction", "Snapshot" };
        foreach (var e in WpfLayerExceptions) m.WpfExceptions.Add(e);
        foreach (var type in ArchTypes(main))
        {
            var owner = OutermostType(type);
            var ownerName = ArchTypeName(owner);
            if (ArchIsSelfTest(type)) continue;
            var generated = ArchIsGenerated(type);
            var lower = ArchLowerLayers.Any(n => ArchInNamespace(owner, n));
            var controller = ArchInNamespace(owner, "TabForge.Controllers");
            var inDiagnostics = ArchInNamespace(owner, "TabForge.Diagnostics");
            var outsideDocuments = !ArchInNamespace(owner, "TabForge.Documents");
            var policeDiagnostics = !inDiagnostics && owner.FullName != "TabForge.Program";

            // delegate-typed host fields in controllers and Views.Score
            if (!generated && (controller || ArchInNamespace(type, "TabForge.Views.Score")))
            {
                var eventNames = new HashSet<string>(type.GetEvents(ArchAllMembers).Select(e => e.Name), StringComparer.Ordinal);
                foreach (var f in type.GetFields(ArchAllMembers))
                    if (!f.IsStatic && typeof(Delegate).IsAssignableFrom(f.FieldType) && !f.IsDefined(typeof(CompilerGeneratedAttribute), false) && !eventNames.Contains(f.Name))
                        m.DelegateHostFields.Add(ArchTypeName(type) + "." + f.Name);
            }
            if (!lower && !controller && !policeDiagnostics && !outsideDocuments) continue;

            foreach (var referenced in ReferencedTypes(type, ref unresolved, (caller, callee) =>
            {
                if (outsideDocuments && callee.DeclaringType == typeof(Documents.UndoController) && undoMethods.Contains(callee.Name))
                {
                    var name = ArchMethodName(caller.DeclaringType ?? type, caller, ArchIsGenerated(caller.DeclaringType ?? type), out _) ?? caller.Name;
                    var key = ownerName + "." + name;
                    m.UndoOutsideDocuments[key] = m.UndoOutsideDocuments.GetValueOrDefault(key) + 1;
                }
            }))
            {
                var rn = referenced.Namespace ?? "";
                if (lower && !m.LayerViolators.ContainsKey(ownerName) &&
                    (ArchUpperLayers.Any(u => ArchInNamespace(referenced, u)) || referenced.FullName is "TabForge.MainWindow" or "TabForge.App"))
                    m.LayerViolators[ownerName] = referenced.FullName ?? referenced.Name;
                if (controller && !m.LayerViolators.ContainsKey(ownerName) &&
                    (referenced.FullName == "TabForge.MainWindow" || typeof(System.Windows.Window).IsAssignableFrom(referenced) ||
                     (ArchInNamespace(referenced, "TabForge.Views") && typeof(System.Windows.UIElement).IsAssignableFrom(referenced))))
                    m.LayerViolators[ownerName] = referenced.FullName ?? referenced.Name;
                if (policeDiagnostics && (ArchTypeName(OutermostType(referenced)) == "TabForge.SelfTest" || ArchInNamespace(referenced, "TabForge.Diagnostics")))
                    m.DiagnosticsReferences.Add(ownerName);
            }
        }
        var probe = typeof(MainWindow).GetNestedType("ProbeAccess", BindingFlags.Public | BindingFlags.NonPublic);
        if (probe is not null) m.ProbeAccessMembers = probe.GetMembers(ArchAllMembers).Count(x => !x.IsDefined(typeof(CompilerGeneratedAttribute), false));
    }

    private static void ArchMeasureSource(ArchMeasured m, string root, Dictionary<string, ArchClassSpec> classSpecs, int maxFileLines)
    {
        var files = ArchSourceFiles(root);
        var listed = classSpecs.ToDictionary(kv => kv.Key, kv => kv.Value.Files.Select(ArchGlob).ToList(), StringComparer.Ordinal);
        var classLines = classSpecs.Keys.ToDictionary(k => k, _ => 0L, StringComparer.Ordinal);
        var classFiles = classSpecs.Keys.ToDictionary(k => k, _ => 0L, StringComparer.Ordinal);

        foreach (var rel in files)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            var lines = ArchLineCount(path);
            var covered = false;
            foreach (var (typeName, globs) in listed)
                if (globs.Any(g => g.IsMatch(rel)))
                { classLines[typeName] += lines; classFiles[typeName]++; covered = true; }
            if (!covered && lines > maxFileLines) m.FilesOverLineCap[rel] = lines;

            var isTest = rel.StartsWith("src/TabForge/SelfTests/", StringComparison.Ordinal) || rel.StartsWith("tests/", StringComparison.Ordinal);
            var text = File.ReadAllText(path);
            var (code, comments) = ArchStrip(text);

            // G6: history comments
            var hist = 0;
            foreach (var (_, raw) in comments)
            {
                var body = Regex.Replace(raw, @"^\s*(///?|/\*+|\*)", "").Trim();
                if (body.Length == 0 || (body.StartsWith('<') && body.EndsWith('>') && body.Length < 20)) continue;
                if (ArchHistoryPatterns.Any(p => p.IsMatch(body))) hist++;
            }
            if (hist > 0) m.HistoryComments[rel] = hist;

            // G8: a project marked dirty outside the document pathway
            if (rel.StartsWith("src/TabForge/", StringComparison.Ordinal) && !isTest &&
                !rel.StartsWith("src/TabForge/Documents/", StringComparison.Ordinal) && rel != "src/TabForge/Services/ProjectService.cs")
            {
                var dirty = Regex.Matches(code, @"\bIsDirty\s*=\s*true\b").Count;
                if (dirty > 0) m.DirtyOutsidePathway[rel] = dirty;
            }

            // G11: the active document read after an await
            var name = Path.GetFileName(rel);
            if ((rel.StartsWith("src/TabForge/Controllers/", StringComparison.Ordinal) || (Path.GetDirectoryName(rel)?.Replace("\\", "/") == "src/TabForge" && name.StartsWith("MainWindow", StringComparison.Ordinal))))
            {
                foreach (Match am in ArchAsyncMethod.Matches(code))
                {
                    var body = ArchBlockAfter(code, am.Index + am.Length);
                    var a = body.IndexOf("await ", StringComparison.Ordinal);
                    if (a < 0) continue;
                    var hits = ArchActiveRead.Matches(body[a..]).Count;
                    if (hits > 0) m.AsyncActiveReads[rel + ":" + am.Groups[1].Value] = hits;
                }
                var lambdaHits = 0;
                foreach (Match lm in ArchAsyncLambda.Matches(code))
                {
                    var body = ArchBlockAfter(code, lm.Index + lm.Length);
                    var a = body.IndexOf("await ", StringComparison.Ordinal);
                    if (a >= 0) lambdaHits += ArchActiveRead.Matches(body[a..]).Count;
                }
                if (lambdaHits > 0) m.AsyncActiveReads[rel + ":lambda"] = lambdaHits;
            }
        }
        foreach (var typeName in classSpecs.Keys)
        {
            m.ClassValues[typeName + "|lines"] = classLines[typeName];
            m.ClassValues[typeName + "|fileCount"] = classFiles[typeName];
        }
    }
}
