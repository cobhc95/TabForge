using System;
using System.Collections.Generic;
using System.IO;
using TabForge.Avalonia.Shell;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Windows-integration setting applied to the file associations (IN-15): registered when wanted, removed when not, nothing when they already
//     match, and a refusal by Windows reported with the registration left as it was. Runs through a fake port, so no registry is written.
public class FileAssociationSyncTests
{
    private const string ExePath = @"C:\Apps\TabForge\TabForge.exe";

    private sealed class FakeAssociations : IFileAssociations
    {
        public bool Registered { get; set; }
        public List<string> Calls { get; } = new();
        public Exception? RefuseWith { get; set; }

        public bool IsRegistered(string exePath) { Calls.Add("is"); return Registered && exePath == ExePath; }

        public void Register(string exePath)
        {
            Calls.Add("register");
            if (RefuseWith is not null) throw RefuseWith;
            Registered = true;
        }

        public void Unregister()
        {
            Calls.Add("unregister");
            Registered = false;
        }
    }

    [Fact]
    public void WantedAndNotRegisteredRegistersTheExecutableAndSaysSo()
    {
        var fake = new FakeAssociations();
        var reports = new List<string>();

        new FileAssociationSync(fake, ExePath, reports.Add).Apply(true);

        Assert.True(fake.Registered);
        Assert.Equal(new[] { "is", "register" }, fake.Calls);
        Assert.Equal(new[] { FileAssociationText.Registered }, reports);
    }

    [Fact]
    public void WantedAndAlreadyRegisteredChangesNothing()
    {
        var fake = new FakeAssociations { Registered = true };
        var reports = new List<string>();

        new FileAssociationSync(fake, ExePath, reports.Add).Apply(true);

        Assert.Equal(new[] { "is" }, fake.Calls);
        Assert.Empty(reports);
    }

    [Fact]
    public void NotWantedAndRegisteredRemovesTheEntriesAndSaysSo()
    {
        var fake = new FakeAssociations { Registered = true };
        var reports = new List<string>();

        new FileAssociationSync(fake, ExePath, reports.Add).Apply(false);

        Assert.False(fake.Registered);
        Assert.Equal(new[] { "is", "unregister" }, fake.Calls);
        Assert.Equal(new[] { FileAssociationText.Removed }, reports);
    }

    /// <summary>A MainWindow built in a test used to register the test runner as the handler for song files in the real registry.</summary>
    [Fact]
    public void AWindowHostedByTheTestRunnerLeavesTheRegistrationAlone()
    {
        var folder = Directory.CreateTempSubdirectory("tabforge-assoc-").FullName;
        var fake = new FakeAssociations();
        try
        {
            using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
            FileAssociationSync.Follow(settings, fake, _ => { });
        }
        finally { Directory.Delete(folder, true); }

        Assert.Empty(fake.Calls);
    }

    [Theory]
    [InlineData(ExePath, true)]
    [InlineData(@"C:\Build\testhost.exe", false)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false)]
    [InlineData("", false)]
    public void OnlyTabForgeItselfMayRegister(string exePath, bool expected) =>
        Assert.Equal(expected, FileAssociationSync.IsAppExecutable(exePath));

    [Fact]
    public void ARefusedWriteIsReportedAndLeavesTheRegistrationAsItWas()
    {
        var fake = new FakeAssociations { RefuseWith = new UnauthorizedAccessException("denied") };
        var reports = new List<string>();

        new FileAssociationSync(fake, ExePath, reports.Add).Apply(true);

        Assert.False(fake.Registered);
        Assert.Equal(new[] { FileAssociationText.Refused }, reports);
    }
}
