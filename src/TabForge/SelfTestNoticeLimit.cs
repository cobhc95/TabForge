using System.IO;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Guitar Pro "notices" are often a long tabber's note (real songs reach 6,000+ characters); the old 4,096-character
/// cap made such songs fail to open ("invalid or overlong notice"). The notice now has its own 64K bound; control
/// characters and oversized text are still refused.
/// </summary>
public static partial class SelfTest
{
    private static void TestNoticeLimit()
    {
        static string Verdict(string notice)
        {
            var project = SingleTrack();
            project.Notice = notice;
            try { ProjectValidator.Validate(project); return ""; }
            catch (InvalidDataException ex) { return ex.Message; }
        }
        var lines = string.Join("\r\n", Enumerable.Range(0, 200).Select(i => "A notice line of ordinary length number " + i));
        Check("a 6,000+ character notice is accepted", lines.Length > 5_000 && Verdict(lines).Length == 0, Verdict(lines));
        Check("a notice over the limit is still refused", Verdict(new string('n', InputLimits.MaxNoticeLength + 1)).Contains("notice", StringComparison.Ordinal));
        Check("a notice with a control character is still refused", Verdict("bad\u0001notice").Contains("notice", StringComparison.Ordinal));
        Check("the instructions keep their 4,096-character bound", Verdict("").Length == 0 && InputLimits.MaxNoticeLength > InputLimits.MaxUserTextLength);
    }
}
