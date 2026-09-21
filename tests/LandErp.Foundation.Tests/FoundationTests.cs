using LandErp.Application.Foundation;
using LandErp.Infrastructure.Persistence;
using LandErp.Server.Foundation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class FoundationTests
{
    [TestMethod]
    public void InvalidConfigurationNeverDisclosesInput()
    {
        const string secret = "synthetic-secret-marker";
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            PersistenceServices.ValidateConnection($"Host=localhost;Password={secret};Include Error Detail=true"));
        Assert.IsFalse(error.ToString().Contains(secret, StringComparison.Ordinal));
        Assert.ThrowsExactly<InvalidOperationException>(() => PersistenceServices.ValidateConnection(""));
        Assert.ThrowsExactly<InvalidOperationException>(() => PersistenceServices.ValidateConnection("malformed"));
    }

    [TestMethod]
    public void CorrelationHeaderRejectsInjectionAndUnboundedValues()
    {
        Assert.IsTrue(CorrelationMiddleware.IsValid("request-123_abc"));
        Assert.IsFalse(CorrelationMiddleware.IsValid("abc\r\nInjected: secret"));
        Assert.IsFalse(CorrelationMiddleware.IsValid(new string('a', 65)));
        Assert.IsFalse(CorrelationMiddleware.IsValid(""));
    }

    [TestMethod]
    public void IdentityTimeAndMoneyConventionsAreUnambiguous()
    {
        Assert.AreEqual('7', DataConventions.NewId().ToString()[14]);
        Assert.AreEqual(1.22m, DataConventions.RoundRubles(1.225m));
        Assert.AreEqual(1.24m, DataConventions.RoundRubles(1.235m));
        Assert.AreEqual(TimeSpan.FromHours(3), TimeZoneInfo.FindSystemTimeZoneById(
            DataConventions.BusinessTimeZoneId).GetUtcOffset(DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void ProjectBoundariesAndStartupMigrationProhibition()
    {
        string root = RepositoryRoot();
        Dictionary<string, string[]> allowed = new(StringComparer.Ordinal)
        {
            ["LandErp.Application"] = ["LandErp.Collector.Contracts"],
            ["LandErp.Collector.Contracts"] = [],
            ["LandErp.Infrastructure"] = ["LandErp.Application"],
            ["LandErp.Server"] = ["LandErp.Application", "LandErp.Infrastructure"],
            ["LandErp.Worker"] = ["LandErp.Application", "LandErp.Infrastructure"]
        };
        foreach ((string project, string[] references) in allowed)
        {
            string directory = Path.Combine(root, "src", project);
            XDocument document = XDocument.Load(Path.Combine(directory, project + ".csproj"));
            foreach (XElement reference in document.Descendants("ProjectReference"))
            {
                string name = Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value);
                CollectionAssert.Contains(references, name);
            }

            foreach (XElement package in document.Descendants("PackageReference"))
            {
                string name = package.Attribute("Include")!.Value;
                Assert.IsFalse(name.Contains("Playwright", StringComparison.Ordinal)
                    || name.Contains("Sqlite", StringComparison.Ordinal));
            }
        }

        foreach (string host in new[] { "LandErp.Server", "LandErp.Worker" })
        {
            string source = File.ReadAllText(Path.Combine(root, "src", host, "Program.cs"));
            Assert.IsFalse(source.Contains("Migrate", StringComparison.Ordinal)
                || source.Contains("EnsureCreated", StringComparison.Ordinal));
        }

        Assert.IsFalse(typeof(IDatabaseStatus).Assembly.GetReferencedAssemblies().Any(assembly =>
            assembly.Name!.Contains("EntityFramework", StringComparison.Ordinal)
            || assembly.Name.Contains("Infrastructure", StringComparison.Ordinal)));
        Assert.IsFalse(typeof(LandErp.ParserSpike.ServerIntegration.ServerAdapter).Assembly.GetReferencedAssemblies().Any(assembly =>
            assembly.Name!.Contains("Npgsql",StringComparison.Ordinal) || assembly.Name.Contains("EntityFramework",StringComparison.Ordinal)
            || assembly.Name.Contains("LandErp.Infrastructure",StringComparison.Ordinal) || assembly.Name.Contains("LandErp.Application",StringComparison.Ordinal)));
    }

    [TestMethod]
    public void IisCertificateRenewalIsTargetedAndHasImmediateAndFallbackPaths()
    {
        string root = RepositoryRoot();
        string sync = File.ReadAllText(Path.Combine(root, "scripts", "Sync-IisCertificate.ps1"));
        string install = File.ReadAllText(Path.Combine(root, "scripts", "Install-IisCertificateRenewal.ps1"));

        StringAssert.Contains(sync, "Get-WebBinding -Name $SiteName -Protocol 'https'");
        StringAssert.Contains(sync, ".AddSslCertificate($thumbprint, 'My')");
        StringAssert.Contains(sync, "$ssl.AuthenticateAsClient($HostName)");
        Assert.IsFalse(sync.Contains("Remove-Item Cert:\\LocalMachine", StringComparison.OrdinalIgnoreCase),
            "Renewal must not delete unrelated certificates.");

        StringAssert.Contains(install, "LandErp IIS Certificate");
        StringAssert.Contains(install, "Certbot-DeployHook.cmd");
        StringAssert.Contains(install, "New-ScheduledTaskTrigger -Daily");
        StringAssert.Contains(install, "Expected exactly one HTTPS binding");
    }

    internal static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LandErp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
