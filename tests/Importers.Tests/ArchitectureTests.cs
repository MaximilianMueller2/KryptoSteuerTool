using KryptoSteuer.Importers;

namespace KryptoSteuer.Importers.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Importers_kennen_weder_Infrastructure_noch_Api_noch_TaxEngine()
    {
        var projektVerweise = ImportersAssembly.Value
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("KryptoSteuer.", StringComparison.Ordinal))
            .ToList();

        projektVerweise.Should().NotContain(
            new[] { "KryptoSteuer.Infrastructure", "KryptoSteuer.Api", "KryptoSteuer.TaxEngine" });
    }
}
