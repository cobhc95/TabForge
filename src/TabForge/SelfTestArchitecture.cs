using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>WPF assemblies the model / playback / services layers must not use (A-04).</summary>
    private static readonly HashSet<string> WpfAssemblies = new(StringComparer.OrdinalIgnoreCase)
        { "PresentationFramework", "PresentationCore", "WindowsBase" };

    /// <summary>Namespaces (and their sub-namespaces) that must stay free of WPF types.</summary>
    private static readonly string[] WpfFreeNamespaces = { "TabForge.Models", "TabForge.Playback", "TabForge.Services" };

    /// <summary>
    /// Sanctioned WPF users inside the WPF-free namespaces, by full type name (nested and compiler-generated types of
    /// these are covered too). Each is a known exception, not a pattern to copy: shrink this list, never grow it casually.
    /// </summary>
    private static readonly HashSet<string> WpfLayerExceptions = new(StringComparer.Ordinal)
    {
        "TabForge.Services.ThemeService",        // the WPF theme / brush service itself (it owns the colour helpers)
        "TabForge.Services.GuitarProExporter",   // track colours parsed through Views.ColourChooser (System.Windows.Media.Color)
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
    /// A-04: the layering rule, enforced. Walks every type's signatures and method-body IL tokens in the TabForge assembly:
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

    /// <summary>The document operations R3 extracted out of MainWindow: they take their document as an argument and know nothing of windows.</summary>
    private static readonly string[] DocumentOperationTypes =
    {
        "TabForge.Documents.DocumentEdits", "TabForge.Documents.DocumentSaveFlow", "TabForge.Documents.DocumentCloseFlow", "TabForge.Documents.DocumentPlacement",
        "TabForge.Documents.DocumentController", "TabForge.Controllers.ArrangementController", "TabForge.Controllers.TrackController",
    };

    /// <summary>
    /// R3 boundaries: the document operations reference no WPF assembly, no view and no window; no static state names a document or a document list
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

    private static Type OutermostType(Type type)
    {
        while (type.DeclaringType is { } outer) type = outer;
        return type;
    }

    /// <summary>Every type a type's declarations and method bodies mention (generic arguments and element types expanded).</summary>
    private static IEnumerable<Type> ReferencedTypes(Type type, ref int unresolved)
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
            try { body = method.GetMethodBody(); } catch { body = null; }
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
                        case MethodInfo m: Add(m.DeclaringType); Add(m.ReturnType); foreach (var p in m.GetParameters()) Add(p.ParameterType); break;
                        case ConstructorInfo c: Add(c.DeclaringType); break;
                    }
                }
                catch (Exception) { unresolved++; }
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
