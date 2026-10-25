using System;
using TabForge.Services;

var denied = @"C:\Users\COBHC\Videos\TabForgeProofParent\Sub";
Console.WriteLine("--- Validate ---");
try { Console.WriteLine("VALIDATE-OK: " + FilePathPolicy.ValidateWritableFolder(denied, "video folder")); }
catch (Exception ex) { Console.WriteLine($"VALIDATE-THREW: {ex.GetType().Name}: {ex.Message}"); }
Console.WriteLine("--- Probe ---");
var ok = FilePathPolicy.TryProbeWritableFolder(denied, out var failure);
Console.WriteLine("PROBE-RESULT: " + ok);
if (failure is not null)
{
    Console.WriteLine($"PROBE-ERR: {failure.GetType().Name}: {failure.Message} HResult=0x{failure.HResult:X8}");
    Console.WriteLine("ISACCESSDENIED: " + FilePathPolicy.IsAccessDenied(failure));
}
Console.WriteLine("--- Fallback ---");
var fb = FilePathPolicy.FallbackWritableFolder("Videos");
Console.WriteLine(fb is null ? "FALLBACK: <NULL>" : "FALLBACK: " + fb);
Console.WriteLine("--- Fallback dir direct probe ---");
var fbDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabForge", "Videos");
Console.WriteLine("FBDIR: " + fbDir);
var ok2 = FilePathPolicy.TryProbeWritableFolder(fbDir, out var failure2);
Console.WriteLine("FB-PROBE-RESULT: " + ok2);
if (failure2 is not null) Console.WriteLine($"FB-PROBE-ERR: {failure2.GetType().Name}: {failure2.Message} HResult=0x{failure2.HResult:X8}");
Console.WriteLine("--- Raw CreateDirectory ---");
try { System.IO.Directory.CreateDirectory(denied); Console.WriteLine("CREATE-OK"); }
catch (Exception ex) { Console.WriteLine($"CREATE-THREW: {ex.GetType().Name}: {ex.Message}"); }
