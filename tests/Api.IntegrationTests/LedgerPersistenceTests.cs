using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace KryptoSteuer.Api.IntegrationTests;

/// <summary>Prüft Migration, Idempotenz-Indizes und die Unveränderlichkeit der Rohdaten gegen echtes PostgreSQL.</summary>
public sealed class LedgerPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static RawRecord Raw(string? externalId = "1", string payload = "{}") => new()
    {
        Source = "bitget", ExternalId = externalId, Payload = payload, FileHash = new string('a', 64),
        ContentHash = KryptoSteuer.Domain.Ledger.IdempotencyKey.Sha256Hex(payload),
        ImportRunId = Guid.NewGuid(), ImportedAtUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Trade_mit_Beinen_wird_mit_voller_Praezision_gespeichert()
    {
        var account = new Account { Name = "Bitget Spot", Type = AccountType.Exchange, Platform = "Bitget" };
        var btc = new Asset { Symbol = "BTC", Class = AssetClass.Crypto };
        var eur = new Asset { Symbol = "EUR", Class = AssetClass.Fiat };
        var tx = new Transaction
        {
            Type = TransactionType.Trade, TimestampUtc = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            AccountId = account.Id, Source = "bitget", ExternalId = "t1",
            Legs =
            [
                new TransactionLeg { AssetId = btc.Id, Amount = 0.123456789012345678m, Direction = LegDirection.In },
                new TransactionLeg { AssetId = eur.Id, Amount = 5000.12345678m, Direction = LegDirection.Out },
            ],
        };
        tx.EnsureValid();

        await using (var db = NewContext())
        {
            db.AddRange(account, btc, eur, tx);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var loaded = await read.Transactions.Include(t => t.Legs).SingleAsync(t => t.Id == tx.Id);
        loaded.Legs.Should().HaveCount(2);
        loaded.Legs.Single(l => l.Direction == LegDirection.In).Amount.Should().Be(0.123456789012345678m);
        loaded.Legs.Single(l => l.Direction == LegDirection.Out).Amount.Should().Be(5000.12345678m);
        loaded.TimestampUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task RawRecord_kann_eingefuegt_aber_nicht_geaendert_werden()
    {
        var raw = Raw("immutable-1");
        await using var db = NewContext();
        db.RawRecords.Add(raw);
        await db.SaveChangesAsync();

        db.Entry(raw).State = EntityState.Modified;

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RawRecord_kann_nicht_geloescht_werden()
    {
        var raw = Raw("immutable-2");
        await using var db = NewContext();
        db.RawRecords.Add(raw);
        await db.SaveChangesAsync();

        db.RawRecords.Remove(raw);

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Gleiche_Quelle_und_externe_ID_wird_von_der_Datenbank_abgelehnt()
    {
        await using var db = NewContext();
        db.RawRecords.Add(Raw("dup-1", "a"));
        await db.SaveChangesAsync();

        await using var second = NewContext();
        second.RawRecords.Add(Raw("dup-1", "b"));

        var act = () => second.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Zeilen_ohne_externe_ID_duerfen_sich_nur_im_Inhalt_unterscheiden()
    {
        await using var db = NewContext();
        db.RawRecords.AddRange(Raw(null, "x"), Raw(null, "y"));

        var act = () => db.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Menge_null_wird_von_der_Datenbank_abgelehnt()
    {
        var account = new Account { Name = "A", Type = AccountType.Wallet, Platform = "Ledger" };
        var btc = new Asset { Symbol = "BTC", Class = AssetClass.Crypto };
        var tx = new Transaction
        {
            Type = TransactionType.Deposit, TimestampUtc = DateTimeOffset.UtcNow, AccountId = account.Id,
            Source = "manual", ExternalId = "zero",
            Legs = [new TransactionLeg { AssetId = btc.Id, Amount = 0m, Direction = LegDirection.In }],
        };
        await using var db = NewContext();
        db.AddRange(account, btc, tx);

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
