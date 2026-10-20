using System.Reflection;

namespace KryptoSteuer.TaxEngine;

/// <summary>Anker für Assembly-Verweise (z. B. in Architekturtests).</summary>
public static class TaxEngineAssembly
{
    public static Assembly Value => typeof(TaxEngineAssembly).Assembly;
}
