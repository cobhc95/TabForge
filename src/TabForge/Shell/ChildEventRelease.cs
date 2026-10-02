using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace TabForge.Shell;

/// <summary>
/// Cuts the references a closed window's child controls hold back to the window. A UI Automation client keeps
/// <c>ElementProxy -> AutomationPeer -> control</c> alive after the window has closed; if the control's event handlers (the window's methods)
/// stay attached, that chain pins the closed window and, through it, its songs. At <c>Closed</c> TabForge's own controls drop their C# events
/// (field-like events) and forget the song (<see cref="ForgetDocument(object)"/>). WPF's routed-event handlers are left alone: the window
/// object itself may stay pinned by a held peer, but it no longer reaches the song. The controls are never used again after the real close.
/// </summary>
internal static class ChildEventRelease
{
    private static readonly Dictionary<Type, FieldInfo[]> EventFields = new();

    /// <summary>Drops the C# events and song references of every TabForge element below <paramref name="root"/> (the root itself keeps its own, it is the window).</summary>
    internal static int Release(DependencyObject root)
    {
        var cleared = 0;
        void Walk(DependencyObject node, bool isRoot)
        {
            if (!isRoot) cleared += ReleaseElement(node);
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i), false);
            if (node is FrameworkElement { ContextMenu: { } menu }) Walk(menu, false);
        }
        Walk(root, true);
        return cleared;
    }

    private static int ReleaseElement(DependencyObject node)
    {
        var cleared = 0;
        var type = node.GetType();
        if (type.Assembly == typeof(ChildEventRelease).Assembly)
        {
            foreach (var field in FieldsOf(type))
                if (field.GetValue(node) is not null) { field.SetValue(node, null); cleared++; }
            cleared += ForgetDocument(node);
        }
        return cleared;
    }

    private static readonly Dictionary<Type, FieldInfo[]> DocumentFields = new();

    private static bool IsDocumentType(Type t) =>
        t.Assembly == typeof(ChildEventRelease).Assembly && (t.Namespace is "TabForge.Models" or "TabForge.Documents");

    private static bool MentionsDocument(Type t) =>
        IsDocumentType(t) || (t.IsArray && MentionsDocument(t.GetElementType()!)) || (t.IsGenericType && t.GetGenericArguments().Any(MentionsDocument));

    /// <summary>
    /// Forgets what a control of a closed window holds about the song: fields of a TabForge model type (or document session), arrays and collections of
    /// them, and callback fields (delegates the window handed the control). After the real close nothing uses them again; a UI Automation client that
    /// still holds the control then pins the control, not the song.
    /// </summary>
    internal static int ForgetDocument(object target) => ForgetDocument(target, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static int ForgetDocument(object target, HashSet<object> seen)
    {
        if (!seen.Add(target)) return 0;
        var type = target.GetType();
        FieldInfo[] fields;
        lock (DocumentFields)
        {
            if (!DocumentFields.TryGetValue(type, out fields!))
            {
                var found = new List<FieldInfo>();
                for (var t = type; t is not null && t.Assembly == typeof(ChildEventRelease).Assembly; t = t.BaseType)
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!f.FieldType.IsValueType) found.Add(f);
                DocumentFields[type] = fields = found.ToArray();
            }
        }
        var cleared = 0;
        foreach (var f in fields)
        {
            var value = f.GetValue(target);
            if (value is null) continue;
            if (MentionsDocument(f.FieldType) || typeof(Delegate).IsAssignableFrom(f.FieldType))
            {
                // A bound view collection (observable: the window's ItemsControls still hold it) is emptied in place; the song model has none.
                // Any other collection is swapped for a fresh empty one, never emptied: it may share a list with a song that lives on (a tab moved
                // to another window), and a non-null field keeps any late handler of the closed control from throwing.
                if (value is System.Collections.Specialized.INotifyCollectionChanged and System.Collections.IList { IsReadOnly: false } bound) bound.Clear();
                else if (value is Array a) f.SetValue(target, Array.CreateInstance(a.GetType().GetElementType()!, a.Length));
                else if (value is System.Collections.IDictionary or System.Collections.IList && value.GetType().GetConstructor(Type.EmptyTypes) is { } ctor) f.SetValue(target, ctor.Invoke(null));
                else f.SetValue(target, null);
                cleared++;
            }
            // Helper objects of a view (its geometry, caches, ...) are owned by it: forget the song in them too. Elements are walked separately.
            else if (value is not DependencyObject && value.GetType().Namespace is { } ns && (ns == "TabForge.Views" || ns.StartsWith("TabForge.Views.", StringComparison.Ordinal)))
                cleared += ForgetDocument(value, seen);
        }
        return cleared;
    }

    /// <summary>The backing fields of field-like events declared by TabForge's own types (a field of the event's delegate type with the event's name).</summary>
    private static FieldInfo[] FieldsOf(Type type)
    {
        lock (EventFields)
        {
            if (EventFields.TryGetValue(type, out var known)) return known;
            var found = new List<FieldInfo>();
            for (var t = type; t is not null && t.Assembly == typeof(ChildEventRelease).Assembly; t = t.BaseType)
                foreach (var e in t.GetEvents(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (t.GetField(e.Name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } backing && backing.FieldType == e.EventHandlerType)
                        found.Add(backing);
            return EventFields[type] = found.ToArray();
        }
    }
}
