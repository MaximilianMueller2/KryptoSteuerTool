using KryptoSteuer.Domain;

namespace KryptoSteuer.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_hat_keine_Verweise_auf_andere_Projekte()
    {
        var projektVerweise = DomainAssembly.Value
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("KryptoSteuer.", StringComparison.Ordinal));

        projektVerweise.Should().BeEmpty("Domain ist reine Fachlogik ohne Abhängigkeiten");
    }
}
