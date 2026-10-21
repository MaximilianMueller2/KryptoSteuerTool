using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Engine;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Pricing;

namespace KryptoSteuer.Application.Tax;

/// <summary>
/// Berechnungslauf: liest Buchungen, Assets und Kurse, ruft die Steuer-Engine auf und speichert Parameter, Hash der Eingabedaten und Ergebnis-Snapshot
/// im <see cref="TaxRun"/>. Jede Neuberechnung zeigt die Unterschiede zur letzten desselben Jahres. Ein Lauf mit offenen Befunden gilt nur als Entwurf.
/// </summary>
public sealed class TaxRunService(ITaxDataStore store, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    /// <summary>Rechnet und speichert einen Lauf.</summary>
    public async Task<TaxRunResult> RunAsync(TaxRunRequest request, CancellationToken cancellationToken)
    {
        var computed = await ComputeAsync(request, cancellationToken);
        var previous = await store.GetLatestRunAsync(request.Year, cancellationToken);

        var snapshot = new
        {
            summary = computed.Summary,
            counts = computed.Counts,
            findings = computed.Result.Findings.Select(f => new { kind = f.Kind.ToString(), f.Message, f.TransactionId, f.AssetId, f.TaxYear }),
        };
        var snapshotJson = JsonSerializer.Serialize(snapshot, Json);

        var run = new TaxRun
        {
            StartedAtUtc = clock.GetUtcNow(),
            TaxYear = request.Year,
            RuleSetVersion = RuleSetLabel(computed.Input),
            ParametersJson = JsonSerializer.Serialize(new
            {
                request,
                lawRuleSet = JsonNode.Parse(computed.Input.Law.ToJson()),
                reformRuleSet = request.ApplyReform2027 ? JsonNode.Parse(computed.Input.Reform.ToJson()) : null,
            }, Json),
            InputHash = computed.InputHash,
            ResultSnapshotJson = snapshotJson,
        };
        await store.AddRunAsync(run, cancellationToken);

        var diff = previous?.ResultSnapshotJson is null ? null : Diff(previous.ResultSnapshotJson, snapshotJson);
        return Build(run, computed, diff, previous is not null && previous.InputHash == computed.InputHash && diff is { Count: 0 });
    }

    public async Task<TaxRunResult?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(id, cancellationToken);
        if (run is null) return null;

        // Der gespeicherte Snapshot ist das Ergebnis; die Anfrage steht in den Parametern.
        var request = JsonSerializer.Deserialize<ParametersEnvelope>(run.ParametersJson, Json)?.Request
            ?? throw new InvalidOperationException("Gespeicherte Parameter sind nicht lesbar.");
        var computed = await ComputeAsync(request, cancellationToken);
        return Build(run, computed, null, computed.InputHash == run.InputHash);
    }

    /// <summary>Rechnet ohne zu speichern (z. B. für die Einzelaufstellung); <paramref name="throughYearEnd"/> schneidet spätere Jahre ab.</summary>
    public async Task<ComputedRun> ComputeAsync(TaxRunRequest request, CancellationToken cancellationToken, bool throughYearEnd = false)
    {
        var transactions = await store.GetTransactionsAsync(cancellationToken);
        // Für Berichte: nur Buchungen bis zum Jahresende (Berlin); der Bestand entspricht dann dem 31.12., spätere Befunde zählen nicht.
        if (throughYearEnd) transactions = transactions.Where(t => KryptoSteuer.TaxEngine.Time.BerlinTime.YearOf(t.TimestampUtc) <= request.Year).ToList();
        var assets = await store.GetAssetsAsync(cancellationToken);
        var priceRows = await store.GetPricesAsync(cancellationToken);

        var book = new InMemoryPriceBook();
        foreach (var p in priceRows) book.Add(p.AssetId, p.TimestampUtc, p.EurPrice, p.Source);

        var decisionRows = await store.GetDecisionsAsync(cancellationToken);
        var decisions = new EngineDecisions(
            decisionRows.Where(d => d is { Kind: ReviewDecisionKind.TransferToOwnAccount, TargetAccountId: not null }).ToDictionary(d => d.TransactionId, d => d.TargetAccountId!.Value),
            decisionRows.Where(d => d.Kind == ReviewDecisionKind.Acknowledged).Select(d => d.TransactionId).ToHashSet());
        var input = TaxEngineInput.Create(transactions, assets, book, request.ToSettings()) with { Decisions = decisions };
        var result = TaxCalculator.Calculate(input);
        var summary = YearlyAggregator.Aggregate(input, result, request.ToYearInputs());
        var counts = new TaxRunCounts(result.Lots.Count, result.Disposals.Count, result.Incomes.Count, result.Futures.Count,
            result.Transfers.Count, result.Findings.Count, transactions.Count);
        return new ComputedRun(input, result, summary, counts, HashOf(transactions, assets, priceRows, decisionRows, request, input));
    }

    public sealed record ComputedRun(TaxEngineInput Input, TaxEngineResult Result, YearSummary Summary, TaxRunCounts Counts, string InputHash);

    private sealed record ParametersEnvelope(TaxRunRequest Request);

    private static TaxRunResult Build(TaxRun run, ComputedRun c, IReadOnlyList<TaxRunDiffEntry>? diff, bool unchanged) =>
        new(run.Id, run.TaxYear, run.StartedAtUtc, run.RuleSetVersion, run.InputHash, unchanged, c.Counts, c.Summary,
            c.Result.Findings, diff, IsDraft: c.Result.Findings.Count > 0);

    private static string RuleSetLabel(TaxEngineInput input) =>
        $"{input.Law.Id}@{input.Law.Version}" + (input.Settings.ApplyReform2027 ? $"+{input.Reform.Id}@{input.Reform.Version}" : string.Empty);

    // ---- Hash der Eingabedaten ----

    /// <summary>SHA-256 über alle Eingaben: Buchungen, Assets, Kurse, Einstellungen und Regelsatz. Gleiche Eingabe ergibt gleichen Hash.</summary>
    private static string HashOf(
        IReadOnlyList<Transaction> transactions, IReadOnlyList<Asset> assets, IReadOnlyList<Price> prices, IReadOnlyList<ReviewDecision> decisions, TaxRunRequest request, TaxEngineInput input)
    {
        var sb = new StringBuilder();
        foreach (var t in transactions.OrderBy(t => t.TimestampUtc).ThenBy(t => t.ExternalId, StringComparer.Ordinal).ThenBy(t => t.Id))
        {
            sb.Append("tx|").Append(t.Id).Append('|').Append(t.Type).Append('|').Append(t.TimestampUtc.UtcTicks).Append('|').Append(t.AccountId)
              .Append('|').Append(t.Status).Append('|').Append(t.ExternalId).Append('|');
            foreach (var l in t.Legs.OrderBy(l => l.Id))
                sb.Append(l.AssetId).Append(':').Append(l.Direction).Append(':').Append(l.Amount.ToString(CultureInfo.InvariantCulture)).Append(l.IsFee ? "F" : "L").Append(';');
            sb.Append('\n');
        }

        foreach (var a in assets.OrderBy(a => a.Id)) sb.Append("asset|").Append(a.Id).Append('|').Append(a.Symbol).Append('|').Append(a.Class).Append('\n');
        foreach (var p in prices.OrderBy(p => p.AssetId).ThenBy(p => p.TimestampUtc).ThenBy(p => p.Source, StringComparer.Ordinal))
            sb.Append("price|").Append(p.AssetId).Append('|').Append(p.TimestampUtc.UtcTicks).Append('|').Append(p.EurPrice.ToString(CultureInfo.InvariantCulture)).Append('|').Append(p.Source).Append('\n');
        foreach (var d in decisions.OrderBy(d => d.TransactionId))
            sb.Append("decision|").Append(d.TransactionId).Append('|').Append(d.Kind).Append('|').Append(d.TargetAccountId).Append('\n');
        sb.Append("request|").Append(JsonSerializer.Serialize(request, Json)).Append('\n');
        sb.Append("rules|").Append(RuleSetLabel(input));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    // ---- Diff zur letzten Berechnung ----

    private static IReadOnlyList<TaxRunDiffEntry> Diff(string beforeJson, string afterJson)
    {
        var before = Flatten(JsonNode.Parse(beforeJson)?["summary"]);
        var after = Flatten(JsonNode.Parse(afterJson)?["summary"]);
        var entries = new List<TaxRunDiffEntry>();
        foreach (var key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            before.TryGetValue(key, out var b);
            after.TryGetValue(key, out var a);
            if (!SameValue(b, a)) entries.Add(new TaxRunDiffEntry(key, b, a));
        }

        return entries;
    }

    private static bool SameValue(string? a, string? b) =>
        a == b || (decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                   && decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && x == y);

    private static Dictionary<string, string> Flatten(JsonNode? node)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(JsonNode? n, string path)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var (k, v) in o) Walk(v, path.Length == 0 ? k : path + "." + k);
                    break;
                case JsonArray arr:
                    for (var i = 0; i < arr.Count; i++) Walk(arr[i], $"{path}[{i}]");
                    break;
                case JsonValue v:
                    map[path] = v.ToString();
                    break;
            }
        }

        Walk(node, string.Empty);
        return map;
    }
}
