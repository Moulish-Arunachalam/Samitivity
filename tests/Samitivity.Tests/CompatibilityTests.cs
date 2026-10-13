using System.Xml.Linq;
using Samitivity.Core;

namespace Samitivity.Tests;
public sealed class CompatibilityTests
{
    private static string Repository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Samitivity.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("Run source compatibility checks from the repository checkout.");
    }
    [Theory]
    [InlineData("10.0.19045", true)] [InlineData("10.0.22631", true)] [InlineData("10.0.26100", true)]
    [InlineData("10.0.10240", true)] [InlineData("10.0.14393", true)]
    [InlineData("10.0.18363", true)] [InlineData("10.0.19044", true)] [InlineData("6.3.9600", false)]
    public void InstallerMinimumAcceptsTargetBuilds(string version, bool accepted)
    {
        var lines = File.ReadAllLines(Path.Combine(Repository(), "installer", "Samitivity.iss"));
        var minimum = Version.Parse(lines.Single(x => x.StartsWith("MinVersion=")).Split('=')[1]);
        Assert.Equal(new Version(10, 0), minimum);
        Assert.Equal(accepted, Version.Parse(version) >= minimum);
        // This verifies the declaration, not execution on these Windows versions.
    }
    [Fact] public void ManifestAndInstallerKeepUnelevatedWindowsTenCompatibilityAndDataIdentity()
    {
        var root = Repository();
        var manifest = XDocument.Load(Path.Combine(root, "src", "Samitivity.Windows", "app.manifest"));
        Assert.Contains(manifest.Descendants(), x => x.Name.LocalName == "supportedOS" && (string?)x.Attribute("Id") == "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}");
        Assert.Contains(manifest.Descendants(), x => x.Name.LocalName == "requestedExecutionLevel" && (string?)x.Attribute("level") == "asInvoker");
        var installer = File.ReadAllText(Path.Combine(root, "installer", "Samitivity.iss"));
        Assert.Contains("PrivilegesRequired=lowest", installer); Assert.Contains("ArchitecturesAllowed=x64compatible", installer);
        Assert.Contains("AppId={{85169B43-476B-4788-BA4E-371D86AF384C}", installer);
        Assert.DoesNotContain("[UninstallDelete]", installer);
        var publish = File.ReadAllText(Path.Combine(root, "scripts", "build-windows.ps1"));
        Assert.Contains("-r win-x64 --self-contained true", publish);
    }
    [Fact] public void ImportDiagnosticsKeepSafeReasonAndNativeCodeWithoutPrivateText()
    {
        var exception = new IOException("private-name-and-path", new System.Runtime.InteropServices.COMException("private recording", unchecked((int)0xC00D36B4)));
        var summary = Diagnostics.FailureSummary(exception);
        Assert.Contains("0xC00D36B4", summary); Assert.DoesNotContain("private", summary);
        Assert.DoesNotContain("private", ImportFailure.Describe(exception));
        var rejected = ImportFailure.Reject("local-source-required");
        Assert.Contains("local disk", ImportFailure.Describe(rejected)); Assert.Contains("local-source-required", Diagnostics.FailureSummary(rejected));
    }
}
