using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Importers.Common;

/// <summary>Baut Buchungen aus einer Rohzeile; gemeinsam für alle Importer.</summary>
internal sealed class ImportTxBuilder(RawRecord record, ImportContext context)
{
    public IReadOnlyList<Transaction> One(Transaction t) => [t];

    public TransactionLeg Leg(string symbol, decimal amount, LegDirection direction, bool isFee = false) =>
        new() { AssetId = context.Assets.Resolve(symbol), Amount = amount, Direction = direction, IsFee = isFee };

    public TransactionLeg Leg(
        string symbol, AssetClass assetClass, string? name, string? isin, decimal amount, LegDirection direction, bool isFee = false) =>
        new() { AssetId = context.Assets.Resolve(symbol, assetClass, name, isin), Amount = amount, Direction = direction, IsFee = isFee };

    public Transaction Build(
        TransactionType type, DateTimeOffset time, List<TransactionLeg> legs,
        TransactionStatus status = TransactionStatus.Ok, string? note = null, string? externalIdSuffix = null) => new()
    {
        Type = type, TimestampUtc = time, AccountId = context.AccountId, Source = record.Source,
        ExternalId = record.ExternalId is null ? null : record.ExternalId + externalIdSuffix,
        // Mehrere Buchungen aus einer Zeile brauchen verschiedene externe IDs (Index auf Quelle + externe ID).
        ContentHash = record.ContentHash, Status = status, Note = note,
        Legs = legs, RawRecordIds = [record.Id],
    };

    /// <summary>Unbekannte Buchungsart: nie verwerfen, sondern NeedsReview mit Rohzeile.</summary>
    public Transaction Review(DateTimeOffset time, string typeName, List<TransactionLeg> legs, string? note = null)
    {
        context.AddUnknownType(typeName);
        return Build(TransactionType.Unknown, time, legs, TransactionStatus.NeedsReview, note ?? $"Unbekannter Typ '{typeName}'");
    }

    /// <summary>Bekannte, aber steuerlich irrelevante Buchung (z. B. interner Transfer): gespeichert mit Status Ignored.</summary>
    public Transaction Ignored(DateTimeOffset time, string typeName, string reason) =>
        Build(TransactionType.Unknown, time, [], TransactionStatus.Ignored, $"{typeName}: {reason}");
}
