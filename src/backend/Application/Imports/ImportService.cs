using System.Security.Cryptography;
using System.Text.Json;
using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Application.Imports;

public enum ImportFailure { NotFound, Conflict, Invalid }

public sealed class ImportException(ImportFailure failure, string message) : Exception(message)
{
    public ImportFailure Failure { get; } = failure;
}

public sealed record ImportRunInfo(
    Guid Id, string Source, string FileName, Guid AccountId, ImportRunStatus Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? CommittedAtUtc, ImportReport Report);

/// <summary>
/// Ablauf: Datei roh speichern (SHA-256), parsen, normalisieren, Duplikate verwerfen, Vorschau liefern;
/// erst nach Bestätigung werden Rohdaten und Buchungen festgeschrieben.
/// </summary>
public sealed class ImportService(IEnumerable<IImporter> importers, IImportStore store, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Analysis(
        ImportReport Report, List<Asset> NewAssets, List<RawRecord> Raws, List<Transaction> Transactions);

    /// <param name="source">Optional: Quelle erzwingen; sonst wählt <see cref="IImporter.CanHandle"/>.</param>
    public async Task<ImportRunInfo> PreviewAsync(
        ImportInput input, Guid accountId, string? source, CancellationToken cancellationToken)
    {
        if (input.Content.Length == 0)
            throw new ImportException(ImportFailure.Invalid, "Die Datei ist leer.");
        if (!await store.AccountExistsAsync(accountId, cancellationToken))
            throw new ImportException(ImportFailure.NotFound, "Das Konto existiert nicht.");

        var importer = string.IsNullOrWhiteSpace(source)
            ? importers.FirstOrDefault(i => i.CanHandle(input))
            : importers.FirstOrDefault(i => string.Equals(i.Source, source, StringComparison.OrdinalIgnoreCase));
        if (importer is null)
            throw new ImportException(ImportFailure.Invalid,
                string.IsNullOrWhiteSpace(source) ? "Kein Importer erkennt diese Datei." : $"Unbekannte Quelle '{source}'.");

        var run = new ImportRun
        {
            Source = importer.Source,
            FileName = input.FileName,
            FileHash = Convert.ToHexString(SHA256.HashData(input.Content)).ToLowerInvariant(),
            Content = input.Content,
            AccountId = accountId,
            CreatedAtUtc = clock.GetUtcNow(),
            ReportJson = "{}",
        };

        var analysis = await AnalyzeAsync(run, importer, cancellationToken);
        run.ReportJson = JsonSerializer.Serialize(analysis.Report, Json);
        await store.AddRunAsync(run, cancellationToken);
        return ToInfo(run, analysis.Report);
    }

    public async Task<ImportRunInfo> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(id, cancellationToken)
            ?? throw new ImportException(ImportFailure.NotFound, "Importlauf nicht gefunden.");
        return ToInfo(run, ReadReport(run));
    }

    public async Task<ImportRunInfo> CommitAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(id, cancellationToken)
            ?? throw new ImportException(ImportFailure.NotFound, "Importlauf nicht gefunden.");
        if (run.Status == ImportRunStatus.Committed)
            throw new ImportException(ImportFailure.Conflict, "Der Importlauf ist bereits bestätigt.");

        var importer = importers.FirstOrDefault(i => i.Source == run.Source)
            ?? throw new ImportException(ImportFailure.Invalid, $"Importer '{run.Source}' ist nicht mehr verfügbar.");

        // Neu analysieren: zwischen Vorschau und Bestätigung können andere Läufe festgeschrieben worden sein.
        var analysis = await AnalyzeAsync(run, importer, cancellationToken);
        run.Status = ImportRunStatus.Committed;
        run.CommittedAtUtc = clock.GetUtcNow();
        run.ReportJson = JsonSerializer.Serialize(analysis.Report, Json);
        await store.CommitAsync(run, analysis.NewAssets, analysis.Raws, analysis.Transactions, cancellationToken);
        return ToInfo(run, analysis.Report);
    }

    /// <summary>Löscht einen unbestätigten Lauf; bestätigte Läufe bleiben (Rohdaten sind unveränderlich).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(id, cancellationToken)
            ?? throw new ImportException(ImportFailure.NotFound, "Importlauf nicht gefunden.");
        if (run.Status == ImportRunStatus.Committed)
            throw new ImportException(ImportFailure.Conflict, "Bestätigte Importläufe können nicht gelöscht werden.");
        await store.DeleteRunAsync(run, cancellationToken);
    }

    private async Task<Analysis> AnalyzeAsync(ImportRun run, IImporter importer, CancellationToken cancellationToken)
    {
        var assets = new AssetCatalog(await store.GetAssetsAsync(cancellationToken));
        var context = new ImportContext(run.AccountId, run.Id, clock.GetUtcNow(), run.FileHash, assets);
        var input = new ImportInput(run.FileName, run.Content);

        var parsed = importer.Parse(input, context).ToList();
        var existing = await store.FindExistingDedupKeysAsync(run.Source, parsed, cancellationToken);

        var seen = new HashSet<string>();
        var raws = new List<RawRecord>();
        var transactions = new List<Transaction>();
        var duplicates = 0;

        foreach (var raw in parsed)
        {
            var key = raw.DedupKey;
            if (existing.Contains(key) || !seen.Add(key))
            {
                duplicates++;
                continue;
            }

            try
            {
                var produced = importer.Normalize(raw, context);
                var invalid = produced.Select(t => t.Validate()).FirstOrDefault(e => e.Count > 0);
                if (invalid is not null)
                {
                    context.AddError(raw.LineNumber, string.Join(" ", invalid));
                    continue;
                }

                raws.Add(raw);
                transactions.AddRange(produced);
            }
            catch (ImportRowException ex)
            {
                context.AddError(raw.LineNumber, ex.Message);
            }
        }

        var report = new ImportReport(
            context.RowsRead,
            transactions.Count,
            duplicates,
            transactions.Count(t => t.Status == TransactionStatus.NeedsReview),
            context.Errors.OrderBy(e => e.LineNumber).ToList(),
            [.. context.UnknownTypes]);

        return new Analysis(report, [.. assets.NewAssets], raws, transactions);
    }

    private static ImportReport ReadReport(ImportRun run) =>
        JsonSerializer.Deserialize<ImportReport>(run.ReportJson, Json)
        ?? new ImportReport(0, 0, 0, 0, [], []);

    private static ImportRunInfo ToInfo(ImportRun run, ImportReport report) =>
        new(run.Id, run.Source, run.FileName, run.AccountId, run.Status, run.CreatedAtUtc, run.CommittedAtUtc, report);
}
