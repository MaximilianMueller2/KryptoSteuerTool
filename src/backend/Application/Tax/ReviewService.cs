using System.Text.Json;
using KryptoSteuer.Application.Accounts;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Model;

namespace KryptoSteuer.Application.Tax;

/// <summary>Port: Entscheidungen der Review-Queue und das Audit-Log dazu.</summary>
public interface IReviewStore
{
    Task<IReadOnlyList<ReviewDecision>> GetDecisionsAsync(CancellationToken cancellationToken);
    Task<Transaction?> GetTransactionAsync(Guid id, CancellationToken cancellationToken);
    Task<ReviewDecision?> GetDecisionAsync(Guid transactionId, CancellationToken cancellationToken);
    /// <summary>Speichert die Entscheidung (ersetzt eine vorhandene) zusammen mit dem Audit-Eintrag.</summary>
    Task SaveAsync(ReviewDecision decision, AuditLogEntry audit, CancellationToken cancellationToken);
    Task RemoveAsync(ReviewDecision decision, AuditLogEntry audit, CancellationToken cancellationToken);
}

public sealed record ReviewItemView(
    string Id, FindingKind Kind, string Message, Guid? TransactionId, Guid? AssetId, Guid? AccountId, int? TaxYear, IReadOnlyList<string> Actions);

public sealed record ReviewList(IReadOnlyList<ReviewItemView> Open, IReadOnlyList<DecisionView> Decisions);

public sealed record DecisionView(Guid TransactionId, ReviewDecisionKind Kind, Guid? TargetAccountId, string? Note, DateTimeOffset CreatedAtUtc);

public sealed class ReviewException(string message) : Exception(message);

/// <summary>
/// Review-Queue: zeigt die offenen Befunde der Steuer-Engine und speichert Entscheidungen des Nutzers als Overrides mit Audit-Log.
/// Rohdaten und Buchungen bleiben unverändert.
/// </summary>
public sealed class ReviewService(IReviewStore store, TaxRunService taxRuns, IAccountRepository accounts, TimeProvider clock)
{
    public const string ActionOwnTransfer = "transfer-to-own-account";
    public const string ActionAcknowledge = "acknowledge";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ReviewList> ListAsync(CancellationToken cancellationToken)
    {
        var computed = await taxRuns.ComputeAsync(new TaxRunRequest { Year = clock.GetUtcNow().Year }, cancellationToken);
        var open = computed.Result.Findings
            .Select(f => new ReviewItemView($"{f.Kind}:{f.TransactionId?.ToString() ?? f.AssetId?.ToString() ?? "-"}", f.Kind, f.Message,
                f.TransactionId, f.AssetId, f.AccountId, f.TaxYear, ActionsFor(f.Kind)))
            .ToList();
        var decisions = (await store.GetDecisionsAsync(cancellationToken))
            .Select(d => new DecisionView(d.TransactionId, d.Kind, d.TargetAccountId, d.Note, d.CreatedAtUtc)).ToList();
        return new ReviewList(open, decisions);
    }

    private static IReadOnlyList<string> ActionsFor(FindingKind kind) => kind switch
    {
        FindingKind.UnmatchedTransferOut => [ActionOwnTransfer],
        FindingKind.TransactionNeedsReview => [ActionAcknowledge],
        _ => [],
    };

    /// <summary>Auszahlung gilt als Transfer auf ein eigenes Konto; ohne <paramref name="targetAccountId"/> wird ein Konto vom Typ Wallet angelegt.</summary>
    public async Task<DecisionView> DecideOwnTransferAsync(
        Guid transactionId, Guid? targetAccountId, string? newAccountName, string? note, CancellationToken cancellationToken)
    {
        var tx = await store.GetTransactionAsync(transactionId, cancellationToken) ?? throw new ReviewException("Buchung nicht gefunden.");
        if (tx.Type is not (TransactionType.TransferOut or TransactionType.Withdrawal))
            throw new ReviewException("Nur Auszahlungen können als Transfer auf ein eigenes Konto markiert werden.");

        Guid target;
        if (targetAccountId is { } id)
        {
            if ((await accounts.ListAsync(cancellationToken)).All(a => a.Id != id)) throw new ReviewException("Zielkonto nicht gefunden.");
            target = id;
        }
        else if (!string.IsNullOrWhiteSpace(newAccountName))
        {
            var account = new Account { Name = newAccountName.Trim(), Type = AccountType.Wallet, Platform = "Wallet" };
            await accounts.AddAsync(account, cancellationToken);
            target = account.Id;
        }
        else throw new ReviewException("Zielkonto oder Name für ein neues Konto angeben.");

        if (target == tx.AccountId) throw new ReviewException("Das Zielkonto muss ein anderes Konto als das Quellkonto sein.");
        return await SaveAsync(tx, ReviewDecisionKind.TransferToOwnAccount, target, note, cancellationToken);
    }

    public async Task<DecisionView> AcknowledgeAsync(Guid transactionId, string? note, CancellationToken cancellationToken)
    {
        var tx = await store.GetTransactionAsync(transactionId, cancellationToken) ?? throw new ReviewException("Buchung nicht gefunden.");
        if (tx.Status != TransactionStatus.NeedsReview) throw new ReviewException("Nur Buchungen zur Prüfung können bestätigt werden.");
        return await SaveAsync(tx, ReviewDecisionKind.Acknowledged, null, note, cancellationToken);
    }

    public async Task<bool> RevokeAsync(Guid transactionId, string? reason, CancellationToken cancellationToken)
    {
        var existing = await store.GetDecisionAsync(transactionId, cancellationToken);
        if (existing is null) return false;
        await store.RemoveAsync(existing, Audit(existing.Id, existing, null, reason ?? "Entscheidung zurückgenommen"), cancellationToken);
        return true;
    }

    private async Task<DecisionView> SaveAsync(Transaction tx, ReviewDecisionKind kind, Guid? target, string? note, CancellationToken cancellationToken)
    {
        var before = await store.GetDecisionAsync(tx.Id, cancellationToken);
        var decision = new ReviewDecision
        {
            Id = before?.Id ?? Guid.NewGuid(), Kind = kind, TransactionId = tx.Id, TargetAccountId = target,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), CreatedAtUtc = clock.GetUtcNow(),
        };
        await store.SaveAsync(decision, Audit(decision.Id, before, decision, note ?? $"Entscheidung {kind}"), cancellationToken);
        return new DecisionView(decision.TransactionId, decision.Kind, decision.TargetAccountId, decision.Note, decision.CreatedAtUtc);
    }

    private AuditLogEntry Audit(Guid entityId, ReviewDecision? before, ReviewDecision? after, string reason) => new()
    {
        TimestampUtc = clock.GetUtcNow(), EntityType = nameof(ReviewDecision), EntityId = entityId,
        BeforeJson = before is null ? null : JsonSerializer.Serialize(before, Json),
        AfterJson = after is null ? null : JsonSerializer.Serialize(after, Json),
        Reason = reason,
    };
}
