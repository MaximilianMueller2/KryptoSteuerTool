using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace KryptoSteuer.Domain;

/// <summary>Anker für Assembly-Verweise (z. B. in Architekturtests).</summary>
[ExcludeFromCodeCoverage]
public static class DomainAssembly
{
    public static Assembly Value => typeof(DomainAssembly).Assembly;
}
