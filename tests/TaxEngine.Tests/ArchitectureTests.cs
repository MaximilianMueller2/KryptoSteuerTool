using KryptoSteuer.TaxEngine;

namespace KryptoSteuer.TaxEngine.Tests;

public class ArchitectureTests
{
    [Fact]
    public void TaxEngine_haengt_nur_von_Domain_ab()
    {
        var projektVerweise = TaxEngineAssembly.Value
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("KryptoSteuer.", StringComparison.Ordinal))
            .ToList();

        projektVerweise.Should().BeSubsetOf(
            new[] { "KryptoSteuer.Domain" },
            "die Steuer-Engine ist reine Logik ohne Datenbank und ohne Infrastruktur");
    }
}
