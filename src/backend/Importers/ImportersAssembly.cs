using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace KryptoSteuer.Importers;

/// <summary>Anker für Assembly-Verweise (z. B. in Architekturtests).</summary>
[ExcludeFromCodeCoverage]
public static class ImportersAssembly
{
    public static Assembly Value => typeof(ImportersAssembly).Assembly;
}
