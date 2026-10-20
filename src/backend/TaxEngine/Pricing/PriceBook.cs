namespace KryptoSteuer.TaxEngine.Pricing;

/// <summary>Ein EUR-Kurs je Einheit mit Quelle (jede Bewertung nennt ihre Quelle).</summary>
public sealed record PriceLookup(decimal EurPrice, string Source);

/// <summary>Kursauskunft für die Engine. Fehlt ein Kurs, liefert sie <c>null</c>; die Engine nimmt dann nie 0 EUR an.</summary>
public interface IPriceBook
{
    PriceLookup? Find(Guid assetId, DateTimeOffset at);
}

/// <summary>
/// Kurse im Speicher. <see cref="Find"/> liefert den letzten Kurs zum Zeitpunkt oder davor, höchstens <see cref="MaxAge"/> alt
/// (Standard 5 Tage, damit Wochenenden und Feiertage bei EZB-Tageskursen überbrückt werden).
/// </summary>
public sealed class InMemoryPriceBook(TimeSpan? maxAge = null) : IPriceBook
{
    private readonly Dictionary<Guid, List<(DateTimeOffset At, decimal Price, string Source)>> _prices = [];

    public TimeSpan MaxAge { get; } = maxAge ?? TimeSpan.FromDays(5);

    public InMemoryPriceBook Add(Guid assetId, DateTimeOffset at, decimal eurPrice, string source)
    {
        if (!_prices.TryGetValue(assetId, out var list)) _prices[assetId] = list = [];
        list.Add((at.ToUniversalTime(), eurPrice, source));
        list.Sort((a, b) => a.At.CompareTo(b.At));
        return this;
    }

    public PriceLookup? Find(Guid assetId, DateTimeOffset at)
    {
        if (!_prices.TryGetValue(assetId, out var list)) return null;
        var t = at.ToUniversalTime();
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].At > t) continue;
            return t - list[i].At <= MaxAge ? new PriceLookup(list[i].Price, list[i].Source) : null;
        }

        return null;
    }
}
