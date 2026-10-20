namespace KryptoSteuer.Domain.Ledger;

public enum AccountType { Exchange, Wallet, Broker }

public enum AssetClass { Crypto, Stablecoin, Fiat, Stock, Etf, Etn, Derivative }

public enum TransactionType
{
    Buy, Sell, Trade, Deposit, Withdrawal, TransferIn, TransferOut,
    Staking, Lending, Airdrop, Mining, Bonus, Interest, Dividend,
    Fee, Spend, GiftIn, GiftOut, FuturesPnL, FundingFee, Liquidation,
}

public enum TransactionStatus { Ok, NeedsReview, Ignored }

public enum LegDirection { In, Out }

/// <summary>Steuerliches Regime eines Lots: Altbestand (§ 23 EStG) oder Neubestand (Reform-Entwurf).</summary>
public enum TaxRegime { Alt, Neu }
