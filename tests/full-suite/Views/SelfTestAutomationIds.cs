using System.Windows;
using System.Windows.Automation;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Stable AutomationIds on the controls scripted UI tests need (ids only; no visible text changed).</summary>
public static partial class SelfTest
{
    private static HashSet<string> AutomationIdsOf(DependencyObject root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Walk(DependencyObject node)
        {
            var id = AutomationProperties.GetAutomationId(node);
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject next) Walk(next);
        }
        Walk(root);
        return ids;
    }

    private static void TestAutomationIds()
    {
        var project = Presets.TemplateFactory.Create("Rock Band");
        var render = new RenderWindow(new RenderContext { Project = project, Settings = new AppSettings(), Engine = TabForge.Audio.AudioEngineClient.Instance }, null);
        var renderIds = AutomationIdsOf(render);
        var wanted = new[] { "Render.Folder", "Render.FileName", "Render.Format", "Render.Source", "Render.Range", "Render.Start", "Render.Cancel" };
        Check("automation ids: the Render window has its folder, file name, format, source, range and button ids",
            wanted.All(renderIds.Contains), string.Join(", ", wanted.Where(id => !renderIds.Contains(id))));
        Check("automation ids: the Render folder box is named 'Output folder' (not its path)",
            AutomationProperties.GetName(FindById(render, "Render.Folder")!) == "Output folder");
        render.Close();

        var options = new PasteOptionsDialog(PasteQuestionInfo.All);
        var optionIds = AutomationIdsOf(options);
        Check("automation ids: Paste options has an id on every radio of every question and on Paste / Cancel",
            optionIds.Contains("Paste.Ok") && optionIds.Contains("Paste.Cancel") &&
            PasteQuestionInfo.All.All(q => optionIds.Contains($"Paste.Q{(int)q}.0")), string.Join(", ", optionIds.OrderBy(i => i)));
        options.Close();

        var special = new PasteSpecialDialog(ScoreClipKind.Beats);
        var specialIds = AutomationIdsOf(special);
        Check("automation ids: Paste special has ids on its mode radios and on Paste / Cancel",
            specialIds.Contains("PasteSpecial.Ok") && specialIds.Contains("PasteSpecial.Cancel") && specialIds.Contains("PasteSpecial.Mode.0"),
            string.Join(", ", specialIds.OrderBy(i => i)));
        special.Close();
    }

    private static DependencyObject? FindById(DependencyObject root, string id)
    {
        if (AutomationProperties.GetAutomationId(root) == id) return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject next && FindById(next, id) is { } found) return found;
        return null;
    }
}
