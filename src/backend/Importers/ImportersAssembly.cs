using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Importers.Bitget;
using KryptoSteuer.Importers.GenericCsv;
using Microsoft.Extensions.DependencyInjection;

namespace KryptoSteuer.Importers;

/// <summary>Anker für Assembly-Verweise (z. B. in Architekturtests).</summary>
[ExcludeFromCodeCoverage]
public static class ImportersAssembly
{
    public static Assembly Value => typeof(ImportersAssembly).Assembly;

    /// <summary>Registriert alle Importer. Neue Quelle: hier eine Zeile ergänzen (siehe docs/04-importe.md).</summary>
    public static IServiceCollection AddImporters(this IServiceCollection services)
    {
        services.AddSingleton<IImporter, GenericCsvImporter>();
        services.AddSingleton<IImporter, BitgetApiImporter>();
        return services;
    }
}
