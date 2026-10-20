using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Application.Imports;

/// <summary>Eingabe eines Imports: Datei bzw. gespeicherte API-Antwort, roh.</summary>
public sealed record ImportInput(string FileName, byte[] Content);

/// <summary>
/// Eine Importquelle (Bitget, Trade Republic, ...). Ablauf: <see cref="Parse"/> liest Rohdaten,
/// <see cref="Normalize"/> macht daraus Buchungen. Unbekannte Typen werden nie verworfen,
/// sondern als <see cref="TransactionType.Unknown"/> mit Status NeedsReview geliefert.
/// </summary>
public interface IImporter
{
    /// <summary>Eindeutiger Quellname, z. B. "bitget-csv". Wird in RawRecord und Transaction gespeichert.</summary>
    string Source { get; }

    bool CanHandle(ImportInput input);

    /// <summary>Liest die Rohzeilen. Zeilen, die sich nicht lesen lassen, werden über <see cref="ImportContext.AddError"/> gemeldet.</summary>
    IEnumerable<RawRecord> Parse(ImportInput input, ImportContext context);

    /// <summary>Wirft <see cref="ImportRowException"/>, wenn die Zeile nicht normalisierbar ist.</summary>
    IReadOnlyList<Transaction> Normalize(RawRecord record, ImportContext context);
}

/// <summary>Eine Zeile ist fehlerhaft; Grund wird mit Zeilennummer im Importbericht gemeldet.</summary>
public sealed class ImportRowException(string reason) : Exception(reason);
