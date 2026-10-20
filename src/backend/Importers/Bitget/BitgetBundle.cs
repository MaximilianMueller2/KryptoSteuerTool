namespace KryptoSteuer.Importers.Bitget;

/// <summary>Konstanten des Rohdaten-Bündels, in dem Sync-Ergebnisse als <c>ImportInput</c> an das Import-Framework gehen.</summary>
public static class BitgetBundle
{
    public const string Format = "bitget-api-bundle/1";
    public const string ClassicAccount = "classic";
    public const string UnifiedAccount = "uta";

    // Abschnittsarten (Feld "kind" je Abschnitt und je RawRecord).
    public const string SpotFills = "spot-fills";
    public const string SpotBills = "spot-bills";
    public const string Deposits = "deposits";
    public const string Withdrawals = "withdrawals";
    public const string FuturesBills = "futures-bills";
    public const string Convert = "convert";
    public const string EarnSavings = "earn-savings";
    public const string UtaFills = "uta-fills";
    public const string UtaFinancial = "uta-financial";
    public const string UtaDeposits = "uta-deposits";
    public const string UtaWithdrawals = "uta-withdrawals";
    public const string UtaConvert = "uta-convert";
}
