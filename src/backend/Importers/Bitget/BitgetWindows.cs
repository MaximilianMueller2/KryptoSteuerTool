namespace KryptoSteuer.Importers.Bitget;

/// <summary>Zeitfenster für die Abfragen: Historie höchstens 90 Tage, je Abfrage höchstens 30 Tage.</summary>
public static class BitgetWindows
{
    public const int MaxHistoryDays = 90;
    public const int MaxWindowDays = 30;

    /// <summary>
    /// Zerlegt [from, to] in Blöcke von höchstens 30 Tagen, von alt nach neu. <c>from</c> wird auf 90 Tage vor <c>to</c> begrenzt.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> Split(DateTimeOffset from, DateTimeOffset to)
    {
        var earliest = to.AddDays(-MaxHistoryDays);
        if (from < earliest) from = earliest;

        var windows = new List<(DateTimeOffset, DateTimeOffset)>();
        for (var start = from; start < to; start = start.AddDays(MaxWindowDays))
        {
            var end = start.AddDays(MaxWindowDays);
            windows.Add((start, end > to ? to : end));
        }

        return windows;
    }
}
