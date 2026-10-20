using System.Text.Json;
using System.Text.Json.Serialization;

namespace KryptoSteuer.TaxEngine.Rules;

/// <summary>Ein Regelsatz-Parameter mit Gültigkeitszeitraum, Rechtsgrundlage und Quelle (jeder Parameter ist belegt).</summary>
/// <param name="Name">Schlüssel, z. B. "Freigrenze23".</param>
/// <param name="Value">Wert als Dezimalzahl (Beträge in EUR, Jahre, Sätze als Anteil).</param>
/// <param name="ValidFromYear">Gültig ab Veranlagungszeitraum (einschließlich).</param>
/// <param name="ValidUntilYear">Gültig bis Veranlagungszeitraum (einschließlich); <c>null</c> = offen.</param>
public sealed record RuleParameter(
    string Name, decimal Value, int ValidFromYear, int? ValidUntilYear, string LegalBasis, string SourceUrl, string? Note = null);

/// <summary>
/// Versionierter Regelsatz. Die Parameter stehen im Code und lassen sich als JSON ausgeben (Snapshot im TaxRun);
/// der Report druckt <see cref="Id"/> und <see cref="Version"/>.
/// </summary>
public sealed record RuleSet(string Id, string Version, string Title, string LegalStatus, IReadOnlyList<RuleParameter> Parameters)
{
    /// <summary>Wert des Parameters für ein Steuerjahr; wirft, wenn es keinen gültigen Eintrag gibt.</summary>
    public decimal Get(string name, int taxYear) =>
        Parameters.Where(p => p.Name == name && p.ValidFromYear <= taxYear && (p.ValidUntilYear is null || taxYear <= p.ValidUntilYear))
            .OrderByDescending(p => p.ValidFromYear)
            .Select(p => (decimal?)p.Value)
            .FirstOrDefault()
        ?? throw new InvalidOperationException($"Regelsatz {Id}: kein Parameter '{name}' für {taxYear}.");

    public RuleParameter Describe(string name, int taxYear) =>
        Parameters.Where(p => p.Name == name && p.ValidFromYear <= taxYear && (p.ValidUntilYear is null || taxYear <= p.ValidUntilYear))
            .OrderByDescending(p => p.ValidFromYear).First();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static RuleSet FromJson(string json) =>
        JsonSerializer.Deserialize<RuleSet>(json, JsonOptions) ?? throw new InvalidOperationException("Regelsatz-JSON ist leer.");
}
