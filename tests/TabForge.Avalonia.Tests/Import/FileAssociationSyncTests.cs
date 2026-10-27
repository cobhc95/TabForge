using System;
using System.Collections.Generic;
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
