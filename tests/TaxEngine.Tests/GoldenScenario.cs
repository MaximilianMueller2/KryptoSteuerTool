using System.Globalization;
using System.Text.Json;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Pricing;
using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.TaxEngine.Tests;

/// <summary>Ein Golden-Test-Szenario: Eingabebuchungen und das von Hand gerechnete Ergebnis (JSON unter Golden/).</summary>
public sealed class GoldenScenario
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public int Year { get; init; }
    public GoldenSettings? Settings { get; init; }
    public GoldenYearInputs? YearInputs { get; init; }
    public List<GoldenAsset> Assets { get; init; } = [];
    public List<GoldenPrice> Prices { get; init; } = [];
    public List<GoldenTransaction> Transactions { get; init; } = [];
    public required GoldenExpected Expected { get; init; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEnumerable<object[]> All() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Golden"), "*.json").Order().Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    public static GoldenScenario Load(string name) =>
        JsonSerializer.Deserialize<GoldenScenario>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", name + ".json")), Json)!;

    // ---- Aufbau der Engine-Eingabe ----

    public static Guid AssetId(string key) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("asset:" + key)));
    public static Guid AccountId(string key) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("account:" + key)));

    public List<Transaction> BuildTransactions() => Transactions.Select(t => new Transaction
    {
        Id = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("tx:" + t.Id))),
        Type = Enum.Parse<TransactionType>(t.Type),
        TimestampUtc = DateTimeOffset.Parse(t.At, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime(),
        AccountId = AccountId(t.Account),
        Source = "golden",
        ExternalId = t.Id,
        Legs = t.Legs.Select(l => new TransactionLeg
        {
            AssetId = AssetId(l.Asset),
            Direction = Enum.Parse<LegDirection>(l.Dir),
            Amount = decimal.Parse(l.Amount, CultureInfo.InvariantCulture),
            IsFee = l.Fee,
        }).ToList(),
    }).ToList();

    public TaxEngineInput BuildInput()
    {
        var assets = Assets.Select(a => new Asset { Id = AssetId(a.Key), Symbol = a.Key, Class = Enum.Parse<AssetClass>(a.Class) }).ToList();
        var prices = new InMemoryPriceBook();
        foreach (var p in Prices)
            prices.Add(AssetId(p.Asset), DateTimeOffset.Parse(p.At, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal), decimal.Parse(p.Eur, CultureInfo.InvariantCulture), "golden");

        var s = Settings;
        var settings = new TaxSettings
        {
            ApplyReform2027 = s?.ApplyReform2027 ?? false,
            Method = s?.Method is { } m ? Enum.Parse<ConsumptionMethod>(m) : ConsumptionMethod.Fifo,
            RegimeOrder = s?.RegimeOrder is { } r ? Enum.Parse<RegimeOrder>(r) : RegimeOrder.Fifo,
            NetworkFees = s?.NetworkFees is { } n ? Enum.Parse<NetworkFeeTreatment>(n) : NetworkFeeTreatment.NeutralWithoutProceeds,
        };
        return TaxEngineInput.Create(BuildTransactions(), assets, prices, settings);
    }

    public YearInputs BuildYearInputs() => new(Year,
        OtherPrivateSalesGainEur: Dec(YearInputs?.OtherPrivateSales),
        LossCarryForwardInEur: Dec(YearInputs?.LossCarryForwardIn));

    private static decimal Dec(string? s) => s is null ? 0m : decimal.Parse(s, CultureInfo.InvariantCulture);
}

public sealed class GoldenSettings
{
    public bool ApplyReform2027 { get; init; }
    public string? Method { get; init; }
    public string? RegimeOrder { get; init; }
    public string? NetworkFees { get; init; }
}

public sealed class GoldenYearInputs
{
    public string? OtherPrivateSales { get; init; }
    public string? LossCarryForwardIn { get; init; }
}

public sealed class GoldenAsset { public required string Key { get; init; } public required string Class { get; init; } }
public sealed class GoldenPrice { public required string Asset { get; init; } public required string At { get; init; } public required string Eur { get; init; } }

public sealed class GoldenTransaction
{
    public required string Id { get; init; }
    public required string At { get; init; }
    public required string Account { get; init; }
    public required string Type { get; init; }
    public List<GoldenLeg> Legs { get; init; } = [];
}

public sealed class GoldenLeg
{
    public required string Asset { get; init; }
    public required string Dir { get; init; }
    public required string Amount { get; init; }
    public bool Fee { get; init; }
}

public sealed class GoldenExpected
{
    public GoldenSection23? Section23 { get; init; }
    public GoldenSection22? Section22 { get; init; }
    public GoldenReform? Reform { get; init; }
    public GoldenFutures? Futures { get; init; }
    public List<GoldenDisposal>? Disposals { get; init; }
    public List<GoldenLot>? Lots { get; init; }
    public Dictionary<string, string>? Holdings { get; init; }
    public List<string>? Findings { get; init; }
    public int? Transfers { get; init; }
    public string? NetworkFeeQuantity { get; init; }
}

public sealed class GoldenSection23
{
    public string? Gains { get; init; } public string? Losses { get; init; } public string? Net { get; init; }
    public string? Freigrenze { get; init; } public bool? BelowFreigrenze { get; init; } public string? Taxable { get; init; }
    public string? LossCarryOut { get; init; } public string? LossUsed { get; init; } public string? TaxFreeAfterPeriodNet { get; init; }
}

public sealed class GoldenSection22
{
    public string? Income { get; init; } public string? Net { get; init; } public bool? BelowFreigrenze { get; init; } public string? Taxable { get; init; }
}

public sealed class GoldenReform
{
    public string? Net { get; init; } public string? Pauschbetrag { get; init; } public string? Taxable { get; init; }
    public string? FlatTax { get; init; } public string? Solidarity { get; init; } public string? Total { get; init; }
}

public sealed class GoldenFutures
{
    public string? RealizedGains { get; init; } public string? RealizedLosses { get; init; } public string? Funding { get; init; }
    public string? TradingFees { get; init; } public string? Liquidation { get; init; } public string? Net { get; init; } public int? Records { get; init; }
}

public sealed class GoldenDisposal
{
    public required string Tx { get; init; }
    public string? LotTx { get; init; }
    public string? Qty { get; init; } public string? Proceeds { get; init; } public string? Cost { get; init; } public string? Fee { get; init; } public string? Gain { get; init; }
    public int? HoldingDays { get; init; } public bool? Within { get; init; } public string? TaxFreeFrom { get; init; }
    public string? Regime { get; init; } public string? Treatment { get; init; }
}

public sealed class GoldenLot
{
    public required string Account { get; init; }
    public required string Asset { get; init; }
    public string? Remaining { get; init; } public string? RemainingCost { get; init; } public string? AcquiredAt { get; init; }
}
