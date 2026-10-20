namespace KryptoSteuer.Application.Imports;

/// <summary>Bericht eines Importlaufs (Vorschau oder Ergebnis).</summary>
/// <param name="RowsRead">Gelesene Datenzeilen.</param>
/// <param name="New">Neue Buchungen (bei der Vorschau: würden geschrieben, nach der Bestätigung: geschrieben).</param>
/// <param name="Duplicates">Zeilen, die schon importiert wurden (Quelle + externe ID bzw. Inhalts-Hash) oder doppelt in der Datei stehen.</param>
/// <param name="NeedsReview">Buchungen mit Status NeedsReview (z. B. unbekannter Typ).</param>
/// <param name="Errors">Fehlerhafte Zeilen mit Zeilennummer und Grund; sie werden nicht geschrieben.</param>
/// <param name="UnknownTypes">Unbekannte Transaktionstypen der Quelle.</param>
/// <param name="Warnings">Hinweise ohne Abbruch (Zeitzone, Kontostand-Lücken, Duplikate aus anderer Quelle).</param>
public sealed record ImportReport(
    int RowsRead,
    int New,
    int Duplicates,
    int NeedsReview,
    IReadOnlyList<ImportError> Errors,
    IReadOnlyList<string> UnknownTypes,
    IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> Warnings { get; init; } = Warnings ?? [];
}
