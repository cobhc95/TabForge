using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>WPF assemblies the model / playback / services layers must not use.</summary>
    private static readonly HashSet<string> WpfAssemblies = new(StringComparer.OrdinalIgnoreCase)
        { "PresentationFramework", "PresentationCore", "WindowsBase" };

    /// <summary>Namespaces (and their sub-namespaces) that must stay free of WPF types.</summary>
    private static readonly string[] WpfFreeNamespaces = { "TabForge.Models", "TabForge.Playback", "TabForge.Services", "TabForge.Documents" };

    /// <summary>
    /// Sanctioned WPF users inside the WPF-free namespaces, by full type name (nested and compiler-generated types of
    /// these are covered too). Each is a known exception, not a pattern to copy: shrink this list, never grow it casually.
    /// </summary>
    private static readonly HashSet<string> WpfLayerExceptions = new(StringComparer.Ordinal)
    {
        "TabForge.Services.ThemeService",        // the WPF theme / brush service itself (it owns the colour helpers)
        "TabForge.Documents.TabItemModel",       // the tab strip's view model binds WPF Visibility and CornerRadius directly
        "TabForge.Documents.TabDragState",       // the tab drag state names the source Window
    };

    /// <summary>
    /// Types outside Program and TabForge.Diagnostics that may use TabForge.AudioEngine directly. Everything else talks to
    /// the engine through TabForge.Audio's client.
    /// </summary>
    private static readonly HashSet<string> EngineReferenceExceptions = new(StringComparer.Ordinal)
    {
        "TabForge.SelfTest",                     // drives the engine headless in-process
        "TabForge.Views.MidiProcessingWindow",   // EM.NoteSetText: the engine's note-set text format, shared by the editor
    };

    /// <summary>
    /// The layering rule, enforced. Walks every type's signatures and method-body IL tokens in the TabForge assembly:
    /// Models / Playback / Services reference no WPF assembly, and TabForge.AudioEngine is referenced only from Program,
    /// TabForge.Diagnostics and the listed exceptions.
    /// </summary>
    private static void TestArchitectureLayering()
    {
        var assembly = typeof(SelfTest).Assembly;
        var wpfOffenders = new SortedSet<string>(StringComparer.Ordinal);
        var engineOffenders = new SortedSet<string>(StringComparer.Ordinal);
        var unresolved = 0;
        var scannedTypes = 0;
        foreach (var type in assembly.GetTypes())
        {
            var owner = OutermostType(type);
            var ns = owner.Namespace ?? "";
            var wpfChecked = WpfFreeNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal)) &&
                             !WpfLayerExceptions.Contains(owner.FullName ?? "");
            var engineChecked = !(owner.FullName == "TabForge.Program" ||
                                  ns == "TabForge.Diagnostics" || ns.StartsWith("TabForge.Diagnostics.", StringComparison.Ordinal) ||
                                  EngineReferenceExceptions.Contains(owner.FullName ?? ""));
            if (!wpfChecked && !engineChecked) continue;
            scannedTypes++;
            foreach (var referenced in ReferencedTypes(type, ref unresolved))
            {
                if (wpfChecked && WpfAssemblies.Contains(referenced.Assembly.GetName().Name ?? ""))
                    wpfOffenders.Add($"{owner.FullName} -> {referenced.FullName ?? referenced.Name}");
                if (engineChecked && referenced.Namespace is { } rns &&
                    (rns == "TabForge.AudioEngine" || rns.StartsWith("TabForge.AudioEngine.", StringComparison.Ordinal)))
                    engineOffenders.Add($"{owner.FullName} -> {referenced.FullName ?? referenced.Name}");
            }
        }
        var stale = WpfLayerExceptions.Concat(EngineReferenceExceptions).Where(n => assembly.GetType(n) is null).ToList();
        Check("every architecture exception still names a type in the TabForge assembly", stale.Count == 0, string.Join(", ", stale));
        Log.Add($"  info  architecture: scanned {scannedTypes} type(s); {WpfLayerExceptions.Count} WPF exception(s), " +
                $"{EngineReferenceExceptions.Count} engine exception(s); {unresolved} IL token(s) not resolvable");
        Check("Models / Playback / Services reference no WPF assembly (PresentationFramework / PresentationCore / WindowsBase)",
            wpfOffenders.Count == 0, Summarise(wpfOffenders));
        Check("TabForge.AudioEngine is referenced only from Program, TabForge.Diagnostics and the listed exceptions",
            engineOffenders.Count == 0, Summarise(engineOffenders));

        // "Owner -> Referenced" pairs grouped by owner: every offending type named, with its first reference as an example.
        static string Summarise(SortedSet<string> pairs) => string.Join("; ", pairs
            .GroupBy(p => p[..p.IndexOf(" -> ", StringComparison.Ordinal)])
            .Select(g => $"{g.Key} ({g.Count()}: {g.First()[(g.Key.Length + 4)..]}, ...)"));
    }

    /// <summary>The document operations: they take their document as an argument and know nothing of windows.</summary>
    private static readonly string[] DocumentOperationTypes =
    {
        "TabForge.Documents.DocumentEdits", "TabForge.Documents.DocumentSaveFlow", "TabForge.Documents.DocumentCloseFlow", "TabForge.Documents.DocumentPlacement",
        "TabForge.Documents.DocumentController", "TabForge.Controllers.ArrangementController", "TabForge.Controllers.TrackController",
    };

    /// <summary>
    /// Boundaries: the document operations reference no WPF assembly, no view and no window; no static state names a document or a document list
    /// (no new "current document"); and the window's save sequence is handed its document (it must not read whichever tab is displayed).
    /// No allow-list: every named type must pass.
    /// </summary>
    private static void TestArchitectureDocumentOperations()
    {
        var assembly = typeof(SelfTest).Assembly;
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        var unresolved = 0;
        foreach (var name in DocumentOperationTypes)
        {
            var type = assembly.GetType(name);
            if (type is null) { missing.Add(name); continue; }
            foreach (var nested in new[] { type }.Concat(assembly.GetTypes().Where(t => OutermostType(t) == type && t != type)))
                foreach (var referenced in ReferencedTypes(nested, ref unresolved))
                {
                    var ns = referenced.Namespace ?? "";
                    if (WpfAssemblies.Contains(referenced.Assembly.GetName().Name ?? "") || ns == "TabForge.Views" || ns.StartsWith("TabForge.Views.", StringComparison.Ordinal) || referenced.FullName == "TabForge.MainWindow")
                        offenders.Add($"{name} -> {referenced.FullName ?? referenced.Name}");
                }
        }
        Check("document operations still exist under the names the architecture check lists", missing.Count == 0, string.Join(", ", missing));
        Check("document operations (edits, save / close / placement flows, controllers) reference no WPF assembly, no view and no MainWindow", offenders.Count == 0, string.Join("; ", offenders.Take(6)));

        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var documentTypes = new[] { typeof(TabForge.Documents.DocumentSession), typeof(TabForge.Documents.DocumentManager) };
        bool NamesDocument(Type t) => documentTypes.Any(d => d.IsAssignableFrom(t));   // a single document or the list; registries keyed by documents (autosave states) are not "the current one"
        var ambient = assembly.GetTypes()
            .Where(t => (t.Namespace ?? "").StartsWith("TabForge.Documents", StringComparison.Ordinal) || (t.Namespace ?? "").StartsWith("TabForge.Controllers", StringComparison.Ordinal) || (t.Namespace ?? "").StartsWith("TabForge.Services", StringComparison.Ordinal) || (t.Namespace ?? "").StartsWith("TabForge.Audio", StringComparison.Ordinal))
            .SelectMany(t => t.GetFields(statics).Where(f => NamesDocument(f.FieldType)).Select(f => $"{t.FullName}.{f.Name}")
                .Concat(t.GetProperties(statics).Where(p => NamesDocument(p.PropertyType)).Select(p => $"{t.FullName}.{p.Name}")))
            .ToList();
        Check("no static field or property in Documents / Controllers / Services / Audio holds a document or the document list (no ambient 'current document')", ambient.Count == 0, string.Join(", ", ambient));

        // The window's save sequence takes its document. A signature without one would read the displayed tab again.
        const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var saveMethods = new[] { "SaveCurrentAsync", "SaveAsAsync", "SaveToAsync", "SaveDocumentAsync", "CollectStatesAsync" };
        var takesNoDocument = saveMethods.Where(n => typeof(MainWindow).GetMethods(instance).Where(m => m.Name == n).Any(m => !m.GetParameters().Any(p => p.ParameterType == typeof(TabForge.Documents.DocumentSession)))).ToList();
        var missingMethods = saveMethods.Where(n => !typeof(MainWindow).GetMethods(instance).Any(m => m.Name == n)).ToList();
        Check("MainWindow's save sequence (save, save as, save to, save document, collect states) takes the document it saves", takesNoDocument.Count == 0 && missingMethods.Count == 0, string.Join(", ", takesNoDocument.Concat(missingMethods)));
        Check("the window's save path no longer keeps its own copy of the per-song audio-save choice (it lives in DocumentSaveFlow)", typeof(MainWindow).GetField("_audioSaveChoice", instance) is null);
        Check("loading a project takes the replace target chosen when the open started (a document, not a flag)",
            typeof(MainWindow).GetMethod("LoadProject", instance)?.GetParameters().Any(p => p.ParameterType == typeof(TabForge.Documents.DocumentSession)) == true);
    }

    /// <summary>
    /// The unified architecture guards (G1 - G14): every measured value is compared with src/TabForge/ArchitectureBudget.json, read from the checkout.
    /// Above budget fails. Below budget logs the line to paste; with TABFORGE_ARCH_STRICT=1 (or "strict-lower", the merge gate) it fails.
    /// TABFORGE_ARCH_RECORD=&lt;file&gt; writes the measured values as a new budget file instead of checking. Registered in the "architecture" group.
    /// </summary>
    private static void TestArchitectureGuards()
    {
        var root = FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "src", "TabForge", "ArchitectureBudget.json");
        if (path is null || !File.Exists(path)) { Skip("architecture guards G1-G14 (ArchitectureBudget.json)", "no source checkout / budget file found", "architecture"); return; }
        var budget = ArchBudget.Load(path);
        var specs = budget.Classes();
        var clock = Stopwatch.StartNew();
        var m = ArchMeasure(root, specs, budget);
        Log.Add($"  info  architecture guards: measured in {clock.ElapsedMilliseconds} ms; IL sizes {(m.IlMeasured ? "measured" : "skipped (unoptimised build)")}; method-size threshold {m.MethodIlThreshold} IL bytes; " +
                $"methods at 1500/2000/2500/3000/3500/4000/5000 bytes: {string.Join("/", m.ThresholdProfile)}");

        var record = Environment.GetEnvironmentVariable("TABFORGE_ARCH_RECORD");
        if (!string.IsNullOrWhiteSpace(record))
        {
            File.WriteAllText(record, ArchRecord(m, specs, budget));
            Check($"architecture budget recorded to the file named by TABFORGE_ARCH_RECORD ({m.ClassValues.Count} class values, {m.MutableStatics.Count} statics, {m.HistoryComments.Count} files with history comments)", true);
            return;
        }

        var mode = Environment.GetEnvironmentVariable("TABFORGE_ARCH_STRICT") ?? "";
        var strict = mode is "1" or "true" or "strict-lower" or "lower";
        var r = new ArchRatchet(strict);

        // G1 layering and G2 WPF-free layers
        r.Sets("G1", "lower layers and Controllers reference no upper layer, window or control", m.LayerViolators.Keys.ToList(), budget.Set("layerExceptions"), "layerExceptions");
        r.Sets("G1", "Controllers and Views.Score hold no host through a delegate field", m.DelegateHostFields, budget.Set("delegateHostFields"), "delegateHostFields");
        r.Sets("G2", "Models, Playback, Services and Documents use no WPF assembly except the listed types", m.WpfExceptions, budget.Set("wpfExceptions"), "wpfExceptions");

        // G3 class budgets and caps
        var missingTypes = specs.Keys.Where(k => typeof(SelfTest).Assembly.GetType(k) is null).ToList();
        Check("G3 every listed class exists in the assembly", missingTypes.Count == 0, string.Join(", ", missingTypes));
        var emptyGlobs = specs.Where(s => m.ClassValues.GetValueOrDefault(s.Key + "|fileCount") == 0).Select(s => s.Key).ToList();
        Check("G3 every listed class names at least one source file", emptyGlobs.Count == 0, string.Join(", ", emptyGlobs));
        var actualClass = new Dictionary<string, long>(StringComparer.Ordinal);
        var budgetClass = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (type, spec) in specs)
            foreach (var metric in new[] { "lines", "fileCount", "fields", "methods", "il" })
            {
                if (metric == "il" && !m.IlMeasured) continue;
                if (spec.Ceiling && metric is "fields" or "methods" or "il") continue;
                if (!spec.Numbers.TryGetValue(metric, out var b)) continue;
                var key = type + "|" + metric;
                budgetClass[key] = b;
                actualClass[key] = m.ClassValues.GetValueOrDefault(key);
            }
        r.Maps("G3", "listed classes (lines, files, fields, methods, IL bytes)", actualClass, budgetClass, "classes",
            exemptFromFall: k => specs.TryGetValue(k[..k.IndexOf('|')], out var s) && s.Ceiling,
            line: (k, v) => $"classes {ArchQuote(k[..k.IndexOf('|')])} \"{k[(k.IndexOf('|') + 1)..]}\": {v}");
        r.Maps("G3", "files over the line cap that belong to no listed class", m.FilesOverLineCap, budget.Map("fileLineExceptions"), "fileLineExceptions");
        r.Maps("G3", "types over the instance-field cap that are not listed", m.TypesOverFieldCap, budget.Map("fieldCapExceptions"), "fieldCapExceptions");

        // G4 method size
        if (m.IlMeasured)
            r.Maps("G4", "methods at or above the IL threshold may not grow", m.LargeMethods, budget.Map("largeMethods"), "largeMethods");
        else Log.Add("  info  G4 method sizes are measured on optimised builds only; skipped here");
        Check("G4 the recorded method-size threshold is the one in use", budget.Number("methodIlThreshold") == m.MethodIlThreshold);

        // G5 mutable statics
        r.Sets("G5", "mutable static fields, properties and events", m.MutableStatics, budget.Set("staticsAllow"), "staticsAllow");
        r.Scalar("G5", "static readonly mutable collections", m.StaticReadonlyCollections, budget.Number("staticReadonlyCollections"), "staticReadonlyCollections");

        // G6 history comments
        r.Maps("G6", "comment lines that narrate history (dates, audit and review IDs, agent names, owner decisions, release narratives)", m.HistoryComments, budget.Map("historyComments"), "historyComments");

        // G7 pins
        if (m.ProbeAccessMembers is { } probeMembers) r.Scalar("G7", "MainWindow.ProbeAccess members", probeMembers, budget.Number("probeAccessMembers"), "probeAccessMembers");
        else Log.Add("  info  G7 MainWindow.ProbeAccess does not exist yet; its member budget starts when it does");
        r.Sets("G7", "only Program and the Diagnostics namespace reference SelfTest or Diagnostics", m.DiagnosticsReferences, budget.Set("diagnosticsReferences"), "diagnosticsReferences");

        // G8 - G12 state ownership
        r.Maps("G8", "projects marked dirty outside Documents and ProjectService", m.DirtyOutsidePathway, budget.Map("dirtyOutsidePathway"), "dirtyOutsidePathway");
        r.Maps("G9", "undo captured outside Documents (callers of Capture, BeginTransaction, Snapshot)", m.UndoOutsideDocuments, budget.Map("undoOutsideDocuments"), "undoOutsideDocuments");
        r.Sets("G10", "window and controller fields holding per-document state", m.WindowDocumentFields, budget.Set("windowDocumentFields"), "windowDocumentFields");
        r.Maps("G11", "active-document reads after an await in the window and controllers", m.AsyncActiveReads, budget.Map("asyncActiveReads"), "asyncActiveReads");
        r.Sets("G12", "static references to song objects and ambient Current / Instance / Shared providers", m.AmbientStatics, budget.Set("ambientStatics"), "ambientStatics");

        // G13 naming: interfaces named *Host start with I; *Controller, *Flow and *Service names are classes
        r.Sets("G13", "types that break the Host / Controller / Flow / Service naming rule", m.NamingViolations, budget.Set("namingExceptions"), "namingExceptions");

        // G14 ownership header: the main class of every Controllers / Services / Views file and every *Flow / *Controller / *Service class
        var headerProblems = OwnershipHeaderProblems(root!, out var viewsWithoutHeader);
        Check("G14 every Controllers / Services class and every *Flow / *Controller / *Service class has an Owns / Does not own / Tests header naming existing tests",
            headerProblems.Count == 0, string.Join("; ", headerProblems.Take(8)) + (headerProblems.Count > 8 ? $" (+{headerProblems.Count - 8} more)" : ""));
        // G14 for Views: the Views main classes that still lack the header are a ratchet against ownershipHeaderExceptions (a new one fails; one that gains its header must leave the list)
        r.Sets("G14", "Views main classes without an Owns / Does not own / Tests header", viewsWithoutHeader, budget.Set("ownershipHeaderExceptions"), "ownershipHeaderExceptions");
    }

    /// <summary>
    /// G14. The classes that need a three-line ownership header (<c>// Owns:</c>, <c>// Does not own:</c>, <c>// Tests:</c>) above their declaration: the main class of
    /// each file under Controllers/, Services/ and Views/ (the class named like the file, else the first; partial parts after the first file are exempt) and every top-level
    /// class named *Flow, *Controller or *Service anywhere in src/TabForge. Playback/, Audio/ and the self-tests are outside the rule. Every Test... name in a header
    /// must be a self-test declared in the checkout. Returns one line per problem. A Views main class without a header is not a problem here: its file is returned in
    /// <paramref name="viewsWithoutHeader"/> and held to the budget list instead.
    /// </summary>
    private static List<string> OwnershipHeaderProblems(string root, out List<string> viewsWithoutHeader)
    {
        var problems = new List<string>();
        viewsWithoutHeader = new List<string>();
        var src = Path.Combine(root, "src", "TabForge");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var testsDir in SelfTestSourceFolders(root))
            foreach (var file in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories))
                foreach (System.Text.RegularExpressions.Match t in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), @"\bstatic\s+(?:async\s+)?[\w<>?,\[\]]+\s+(Test\w+)\s*\("))
                    declared.Add(t.Groups[1].Value);
        var classRx = new System.Text.RegularExpressions.Regex(@"^(?:public |internal )?(?:static |sealed |abstract |partial )*class (\w+)", System.Text.RegularExpressions.RegexOptions.Multiline);
        var suffixRx = new System.Text.RegularExpressions.Regex(@"(Flow|Controller|Service)$");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (rel.StartsWith("bin/") || rel.StartsWith("obj/") || rel.StartsWith("Playback/") || rel.StartsWith("Audio/") || rel.StartsWith("SelfTests/")) continue;
            var text = File.ReadAllText(file).Replace("\r\n", "\n");
            var classes = classRx.Matches(text);
            if (classes.Count == 0) continue;
            var required = new List<System.Text.RegularExpressions.Match>();
            var stem = Path.GetFileNameWithoutExtension(file);
            var inViews = rel.StartsWith("Views/", StringComparison.Ordinal);
            System.Text.RegularExpressions.Match? main = null;
            if ((rel.StartsWith("Controllers/") || rel.StartsWith("Services/") || inViews) && !stem.Contains('.'))
            {
                main = classes.FirstOrDefault(c => c.Groups[1].Value == stem) ?? classes[0];
                required.Add(main);
            }
            foreach (System.Text.RegularExpressions.Match c in classes)
                if (suffixRx.IsMatch(c.Groups[1].Value) && !required.Contains(c)) required.Add(c);
            foreach (var c in required)
            {
                // The comment lines directly above the declaration, skipping its attributes and XML documentation.
                var above = text[..c.Index].Split('\n').ToList();
                above.RemoveAt(above.Count - 1);
                var i = above.Count;
                while (i > 0 && (above[i - 1].TrimStart().StartsWith("///") || above[i - 1].TrimStart().StartsWith("["))) i--;
                var header = new List<string>();
                while (i > 0 && above[i - 1].TrimStart().StartsWith("//") && !above[i - 1].TrimStart().StartsWith("///")) header.Insert(0, above[--i].Trim());
                var block = string.Join("\n", header);
                var name = $"{rel}: {c.Groups[1].Value}";
                if (!block.Contains("// Owns:") || !block.Contains("// Does not own:") || !block.Contains("// Tests:"))
                {
                    if (inViews && ReferenceEquals(c, main)) viewsWithoutHeader.Add(rel);
                    else problems.Add($"{name} lacks the Owns / Does not own / Tests header");
                    continue;
                }
                foreach (System.Text.RegularExpressions.Match t in System.Text.RegularExpressions.Regex.Matches(block, @"\bTest[A-Z]\w*"))
                    if (!declared.Contains(t.Value)) problems.Add($"{name} names {t.Value}, which is not a self-test");
            }
        }
        return problems;
    }

    /// <summary>
    /// Every test registered with Guard / GuardGroup has an explicit entry in <c>AreaOf</c>, so a focused <c>--areas</c> run never depends on a
    /// default. The registrations are read from the self-test sources of the checkout; "core" is a valid, explicit area (always runs).
    /// </summary>
    private static void TestEveryTestHasAnArea()
    {
        var root = FindRepositoryRoot();
        var dir = root is null ? null : Path.Combine(root, "src", "TabForge", "SelfTests");
        if (dir is null || !Directory.Exists(dir)) { Skip("every registered self-test has an AreaOf entry", "no source checkout found", "architecture"); return; }
        var registered = new SortedSet<string>(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var areaKeys = new HashSet<string>(AreaOf.Keys, StringComparer.Ordinal);   // plus the full suite's table, read from its source
        foreach (var file in SelfTestSourceFolders(root!).SelectMany(f => Directory.EnumerateFiles(f, "*.cs", SearchOption.AllDirectories)))
        {
            var raw = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(raw, @"\[""(Test\w+)""\]\s*=\s*"""))
                areaKeys.Add(m.Groups[1].Value);
            var (code, _) = ArchStrip(raw);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(code, @"\bGuard(?:Group)?\(\s*(?:[^,()]*,\s*)?(Test\w+)\s*[,)]"))
                registered.Add(m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(code, @"\bstatic\s+(?:async\s+)?[\w<>?,\[\]]+\s+(Test\w+)\s*\("))
                declared.Add(m.Groups[1].Value);
        }
        var missing = registered.Where(n => !areaKeys.Contains(n)).ToList();
        Check($"every registered self-test ({registered.Count}) has an explicit AreaOf entry", registered.Count > 10 && missing.Count == 0,
            registered.Count <= 10 ? "the registration scan found too few tests" : $"{missing.Count} without an area: {string.Join(", ", missing.Take(12))}");
        var stale = areaKeys.Where(k => !declared.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Check("every AreaOf entry names an existing self-test", stale.Count == 0, string.Join(", ", stale.Take(12)));
    }

    /// <summary>The folders holding self-test sources: the basic set under src, and tests/full-suite when the checkout has it.</summary>
    private static IEnumerable<string> SelfTestSourceFolders(string root)
    {
        var basic = Path.Combine(root, "src", "TabForge", "SelfTests");
        if (Directory.Exists(basic)) yield return basic;
        var full = Path.Combine(root, "tests", "full-suite");
        if (Directory.Exists(full)) yield return full;
    }

    private static Type OutermostType(Type type)
    {
        while (type.DeclaringType is { } outer) type = outer;
        return type;
    }

    /// <summary>Every type a type's declarations and method bodies mention (generic arguments and element types expanded).</summary>
    private static IEnumerable<Type> ReferencedTypes(Type type, ref int unresolved, Action<MethodBase, MethodInfo>? onCall = null)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var found = new HashSet<Type>();
        void Add(Type? t)
        {
            if (t is null || t.IsGenericParameter || !found.Add(t)) return;
            if (t.HasElementType) Add(t.GetElementType());
            if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) Add(a);
        }
        Add(type.BaseType);
        foreach (var i in type.GetInterfaces()) Add(i);
        foreach (var f in type.GetFields(all)) Add(f.FieldType);
        foreach (var p in type.GetProperties(all)) Add(p.PropertyType);
        foreach (var ev in type.GetEvents(all)) Add(ev.EventHandlerType);
        var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
        foreach (var method in methods)
        {
            if (method is MethodInfo mi) Add(mi.ReturnType);
            foreach (var p in method.GetParameters()) Add(p.ParameterType);
            MethodBody? body;
            try { body = method.GetMethodBody(); } catch { body = null; } // Not logged: self-test harness: the failure is recorded as a check result
            if (body is null) continue;
            foreach (var local in body.LocalVariables) Add(local.LocalType);
            var il = body.GetILAsByteArray();
            if (il is null) continue;
            var typeArgs = type.IsGenericType ? type.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            foreach (var token in IlMemberTokens(il))
            {
                try
                {
                    var member = type.Module.ResolveMember(token, typeArgs, methodArgs);
                    switch (member)
                    {
                        case Type t: Add(t); break;
                        case FieldInfo f: Add(f.DeclaringType); Add(f.FieldType); break;
                        case MethodInfo m: onCall?.Invoke(method, m); Add(m.DeclaringType); Add(m.ReturnType); foreach (var p in m.GetParameters()) Add(p.ParameterType); break;
                        case ConstructorInfo c: Add(c.DeclaringType); break;
                    }
                }
                catch (Exception) { unresolved++; } // Not logged: self-test harness: the failure is recorded as a check result
            }
        }
        return found;
    }

    private static OpCode[]? _oneByteOps, _twoByteOps;

    /// <summary>The metadata tokens of the type / field / method operands in an IL stream.</summary>
    private static IEnumerable<int> IlMemberTokens(byte[] il)
    {
        if (_oneByteOps is null)
        {
            _oneByteOps = new OpCode[256];
            _twoByteOps = new OpCode[256];
            foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.GetValue(null) is not OpCode op) continue;
                var value = (ushort)op.Value;
                if (value < 0x100) _oneByteOps[value] = op;
                else if ((value & 0xFF00) == 0xFE00) _twoByteOps[value & 0xFF] = op;
            }
        }
        var tokens = new List<int>();
        var i = 0;
        while (i < il.Length)
        {
            var op = il[i] == 0xFE && i + 1 < il.Length ? _twoByteOps![il[++i]] : _oneByteOps[il[i]];
            i++;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: i += 1; break;
                case OperandType.InlineVar: i += 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: i += 8; break;
                case OperandType.InlineSwitch: i += 4 + 4 * BitConverter.ToInt32(il, i); break;
                case OperandType.InlineField: case OperandType.InlineMethod: case OperandType.InlineTok: case OperandType.InlineType:
                    tokens.Add(BitConverter.ToInt32(il, i)); i += 4; break;
                default: i += 4; break;   // InlineBrTarget, InlineI, InlineSig, InlineString, ShortInlineR
            }
        }
        return tokens;
    }
}
