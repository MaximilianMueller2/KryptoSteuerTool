using System.Security.Cryptography;
using System.Text;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Rules;
using KryptoSteuer.TaxEngine.Time;

namespace KryptoSteuer.TaxEngine.Engine;

/// <summary>
/// Lot-Engine (§ 23 EStG): Zugänge erzeugen Lots mit Anschaffungszeitpunkt und Kosten in EUR, Abgänge verbrauchen Lots je Konto
/// (FIFO oder Durchschnitt) und erzeugen je verbrauchtem Lot einen Disposal-Datensatz. Eigene Transfers verschieben Lots mit
/// ursprünglichem Datum und Kosten. Reine Logik ohne Datenbank; gleiche Eingabe ergibt das gleiche Ergebnis
/// (Sortierung nach Zeitstempel, dann externer ID, dann ID). Regeln: docs/05-steuerregeln.md.
/// </summary>
public static class TaxCalculator
{
    public static TaxEngineResult Calculate(TaxEngineInput input) => new Run(input).Execute();

    /// <summary>Beginn des Regimes "Neu": 01.01.2027 00:00:00 Europe/Berlin (Anschaffung nach dem 31.12.2026 23:59:59).</summary>
    public static readonly DateOnly NewRegimeFrom = new(2027, 1, 1);

    private sealed class Run(TaxEngineInput input)
    {
        private const decimal Dust = 0.000000000001m;

        private readonly TaxSettings _settings = input.Settings;
        private readonly IConsumptionStrategy _strategy = input.Settings.Method == ConsumptionMethod.AverageCost
            ? new AverageCostStrategy() : new FifoStrategy();

        private readonly Dictionary<(Guid Account, Guid Asset), List<LotState>> _lots = [];
        private readonly List<DisposalRecord> _disposals = [];
        private readonly List<IncomeRecord> _incomes = [];
        private readonly List<FuturesRecord> _futures = [];
        private readonly List<TransferRecord> _transfers = [];
        private readonly List<NetworkFeeRecord> _networkFees = [];
        private readonly List<ReviewFinding> _findings = [];
        private readonly HashSet<Guid> _blocked = [];
        private readonly Dictionary<Guid, int> _skippedWhileBlocked = [];

        private readonly Dictionary<Guid, TransferMatch> _matchByOut = [];
        private readonly Dictionary<Guid, TransferMatch> _matchByIn = [];
        private readonly Dictionary<Guid, List<TransitPiece>> _transit = [];
        private int _sequence;

        private sealed record TransitPiece(
            DateTimeOffset AcquiredAtUtc, decimal Quantity, decimal CostEur, TaxRegime Regime, LotKind Kind, Guid SourceTransactionId, string? SourceExternalId);

        // ---- Hilfen: Assets und Bewertung ----

        private Asset? AssetOf(Guid id) => input.Assets.GetValueOrDefault(id);
        private bool IsFiat(Guid id) => AssetOf(id)?.Class == AssetClass.Fiat;
        private bool IsEur(Guid id) => AssetOf(id) is { Class: AssetClass.Fiat } a && a.Symbol.Equals("EUR", StringComparison.OrdinalIgnoreCase);
        private bool IsTracked(Guid id) => AssetOf(id)?.Class is AssetClass.Crypto or AssetClass.Stablecoin;
        private string Sym(Guid id) => AssetOf(id)?.Symbol ?? id.ToString("N")[..8];

        private TaxRegime RegimeOf(Guid assetId, DateTimeOffset acquiredAt) =>
            BerlinTime.DateOf(acquiredAt) >= NewRegimeFrom && AssetOf(assetId) is { } a && _settings.IsTauschkrypto(a.Symbol)
                ? TaxRegime.Neu : TaxRegime.Alt;

        private void Finding(FindingKind kind, string message, Transaction? tx = null, Guid? assetId = null) =>
            _findings.Add(new ReviewFinding(kind, message, tx?.Id, assetId, tx?.AccountId,
                tx is null ? null : BerlinTime.YearOf(tx.TimestampUtc)));

        private bool IsBlocked(Transaction tx, params Guid[] assets)
        {
            var hit = assets.Where(_blocked.Contains).ToList();
            if (hit.Count == 0) return false;
            foreach (var a in hit) _skippedWhileBlocked[a] = _skippedWhileBlocked.GetValueOrDefault(a) + 1;
            return true;
        }

        private void Block(Guid assetId) => _blocked.Add(assetId);

        /// <summary>EUR-Wert einer Menge: Kurs aus der Kursliste (nie 0 bei fehlendem Kurs).</summary>
        private (decimal Value, string Source)? ValueOf(Guid assetId, decimal quantity, DateTimeOffset at)
        {
            if (IsEur(assetId)) return (quantity, "EUR");
            var p = input.Prices.Find(assetId, at);
            return p is null ? null : (p.EurPrice * quantity, p.Source);
        }

        private static Guid Id(params object[] parts)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)))));
            return new Guid(hash);
        }

        // ---- Lauf ----

        public TaxEngineResult Execute()
        {
            var ordered = input.Transactions
                .Where(t => t.Status != TransactionStatus.Ignored)
                .OrderBy(t => t.TimestampUtc)
                .ThenBy(t => t.ExternalId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(t => t.Id)
                .ToList();

            var match = TransferMatcher.Match(ordered, input.Assets, _settings);
            foreach (var m in match.Matches) { _matchByOut[m.Out.Id] = m; _matchByIn[m.In.Id] = m; }
            foreach (var (tx, hint) in match.UnmatchedOut)
                Finding(FindingKind.UnmatchedTransferOut,
                    $"Auszahlung ohne Gegenstück ({Describe(tx)}). Die Lots bleiben im Quellkonto. {hint} Entscheidung nötig: eigenes externes Konto, Verkauf außerhalb, Schenkung oder Verlust.", tx, AssetIdOf(tx));
            foreach (var tx in match.UnmatchedIn)
                Finding(FindingKind.UnmatchedTransferIn,
                    $"Eingang ohne Gegenstück ({Describe(tx)}): Anschaffungsdatum und -kosten sind unbekannt, es wird kein Lot angelegt (nie mit 0 EUR Kosten).", tx, AssetIdOf(tx));

            foreach (var tx in ordered)
                Process(tx);

            foreach (var (asset, range) in _missingFuturesRates)
                _findings.Add(new ReviewFinding(FindingKind.MissingPrice,
                    $"EUR-Kurs für {Sym(asset)} fehlt für {range.Count} Futures-Buchungen zwischen {range.From:yyyy-MM-dd} und {range.To:yyyy-MM-dd}. " +
                    "Diese Buchungen fehlen in der Futures-Summe; es wird nie 0 EUR angenommen. Kurse importieren (EZB-Referenzkurse) und neu berechnen.",
                    AssetId: asset));

            foreach (var (asset, count) in _skippedWhileBlocked)
                _findings.Add(new ReviewFinding(FindingKind.AssetBlocked,
                    $"Die Berechnung für {Sym(asset)} wurde gestoppt; {count} spätere Buchungen wurden übersprungen. Erst die zugehörige Prüfung lösen.", AssetId: asset));

            var lots = _lots.Values.SelectMany(x => x).OrderBy(l => l.Sequence).Select(ToRecord).ToList();
            var holdings = _lots.ToDictionary(kv => (kv.Key.Account, kv.Key.Asset), kv => kv.Value.Sum(l => l.Remaining));
            return new TaxEngineResult(lots, _disposals, _incomes, _futures, _transfers, _networkFees, _findings, holdings);
        }

        private static string Describe(Transaction tx) =>
            $"{tx.Type} am {tx.TimestampUtc:yyyy-MM-dd HH:mm:ss}Z, Quelle {tx.Source}, ID {tx.ExternalId ?? tx.Id.ToString("N")[..8]}";

        private Guid? AssetIdOf(Transaction tx) => tx.Legs.FirstOrDefault(l => !l.IsFee)?.AssetId;

        private void Process(Transaction tx)
        {
            if (tx.Status == TransactionStatus.NeedsReview)
            {
                Finding(FindingKind.TransactionNeedsReview, $"Buchung zur Prüfung: {tx.Note ?? tx.Type.ToString()} ({Describe(tx)}).", tx, AssetIdOf(tx));
                return;
            }

            switch (tx.Type)
            {
                case TransactionType.Buy or TransactionType.Sell or TransactionType.Trade:
                    ProcessSwap(tx);
                    break;
                case TransactionType.Spend:
                    ProcessSpend(tx);
                    break;
                case TransactionType.TransferOut or TransactionType.Withdrawal:
                    ProcessTransferOut(tx);
                    break;
                case TransactionType.TransferIn or TransactionType.Deposit:
                    ProcessTransferIn(tx);
                    break;
                case TransactionType.Staking or TransactionType.Lending or TransactionType.Interest
                    or TransactionType.Airdrop or TransactionType.Mining or TransactionType.Bonus:
                    ProcessIncome(tx);
                    break;
                case TransactionType.GiftIn or TransactionType.GiftOut:
                    if (tx.Legs.Any(l => !l.IsFee && IsTracked(l.AssetId)))
                        Finding(FindingKind.DonationNeedsData,
                            "Schenkung von Krypto: Datum und Kosten des Vorbesitzers (Eingang) bzw. Abgang ohne Veräußerung müssen angegeben werden.", tx, AssetIdOf(tx));
                    break;
                case TransactionType.FuturesPnL or TransactionType.FundingFee or TransactionType.Liquidation or TransactionType.Fee:
                    ProcessFutures(tx);
                    break;
                default:
                    // Steuer, Dividende (EUR) und Ähnliches betreffen nicht die Krypto-Lots.
                    break;
            }
        }

        // ---- Kauf, Verkauf, Tausch ----

        private void ProcessSwap(Transaction tx)
        {
            var main = tx.Legs.Where(l => !l.IsFee).ToList();
            var inLeg = main.FirstOrDefault(l => l.Direction == LegDirection.In);
            var outLeg = main.FirstOrDefault(l => l.Direction == LegDirection.Out);
            var feeLeg = tx.Legs.FirstOrDefault(l => l.IsFee);
            if (inLeg is null || outLeg is null) return;

            var inA = inLeg.AssetId;
            var outA = outLeg.AssetId;
            var inTracked = IsTracked(inA);
            var outTracked = IsTracked(outA);

            if (!inTracked && !outTracked) return; // z. B. Wertpapiere gegen EUR: Aktien/ETF-Modul
            if ((inTracked && !outTracked && !IsEur(outA)) || (outTracked && !inTracked && !IsEur(inA)))
            {
                Finding(FindingKind.UnsupportedAsset, $"Tausch zwischen {Sym(outA)} und {Sym(inA)} wird nicht unterstützt (nur Krypto, Stablecoins und EUR).", tx, inTracked ? inA : outA);
                return;
            }

            if (IsBlocked(tx, inTracked ? inA : outA, outTracked ? outA : inA)) return;

            var inQty = inLeg.Amount;
            var outQty = outLeg.Amount;
            var time = tx.TimestampUtc;

            // Gebühr: in EUR, im erhaltenen Coin, im hingegebenen Coin oder in einem dritten Coin.
            decimal feeEur = 0m;
            decimal inQtyNet = inQty;
            decimal outQtyTotal = outQty;
            string? feeSource = null;
            if (feeLeg is not null)
            {
                var fa = feeLeg.AssetId;
                if (IsEur(fa)) feeEur = feeLeg.Amount;
                else if (fa == inA && inTracked) inQtyNet = inQty - feeLeg.Amount;
                else if (fa == outA && outTracked) outQtyTotal = outQty + feeLeg.Amount;
                else if (IsTracked(fa))
                {
                    // Gebühr in einem dritten Coin: eigener Abgang dieses Coins, dessen Wert die Kosten bzw. den Erlös beeinflusst.
                    if (IsBlocked(tx, fa)) return;
                    var v = ValueOf(fa, feeLeg.Amount, time);
                    if (v is null)
                    {
                        MissingPrice(tx, fa, "Gebühr in einem dritten Coin");
                        Block(fa); Block(inTracked ? inA : outA); Block(outTracked ? outA : inA);
                        return;
                    }

                    if (!Dispose(tx, tx.AccountId, fa, feeLeg.Amount, v.Value.Value, 0m, v.Value.Source, "Gebühr in drittem Coin: eigener Abgang")) return;
                    feeEur = v.Value.Value;
                    feeSource = v.Value.Source;
                }
                else
                {
                    Finding(FindingKind.UnsupportedAsset, $"Gebühr in {Sym(fa)} wird nicht unterstützt.", tx, fa);
                    return;
                }
            }

            if (inQtyNet <= 0 || outQtyTotal <= 0) { Finding(FindingKind.Other, "Menge nach Abzug der Gebühr ist nicht positiv.", tx); return; }

            if (IsEur(outA) && inTracked)
            {
                // Kauf gegen EUR: Anschaffungskosten = EUR-Betrag plus Gebühr.
                Acquire(tx, inA, inQtyNet, outQty + feeEur, LotKind.Purchase, time);
            }
            else if (IsEur(inA) && outTracked)
            {
                // Verkauf gegen EUR: Gebühr mindert den Erlös.
                Dispose(tx, tx.AccountId, outA, outQtyTotal, inQty, feeEur, "EUR", null);
            }
            else if (inTracked && outTracked)
            {
                // Tausch (Krypto→Krypto, →Stablecoin): Wert des Erhaltenen, ersatzweise des Hingegebenen (BMF-Schreiben vom 06.03.2025).
                var v = ValueOf(inA, inQty, time) ?? ValueOf(outA, outQty, time);
                if (v is null)
                {
                    MissingPrice(tx, inA, $"Tausch {Sym(outA)}→{Sym(inA)}");
                    Block(inA); Block(outA);
                    return;
                }

                if (Dispose(tx, tx.AccountId, outA, outQtyTotal, v.Value.Value, feeEur, v.Value.Source, $"Tausch gegen {Sym(inA)}"))
                    Acquire(tx, inA, inQtyNet, v.Value.Value + feeEur, LotKind.Swap, time);
            }

            _ = feeSource;
        }

        private void ProcessSpend(Transaction tx)
        {
            var outLeg = tx.Legs.FirstOrDefault(l => !l.IsFee && l.Direction == LegDirection.Out);
            if (outLeg is null || !IsTracked(outLeg.AssetId) || IsBlocked(tx, outLeg.AssetId)) return;

            var v = ValueOf(outLeg.AssetId, outLeg.Amount, tx.TimestampUtc);
            if (v is null) { MissingPrice(tx, outLeg.AssetId, "Bezahlen mit Krypto"); Block(outLeg.AssetId); return; }
            Dispose(tx, tx.AccountId, outLeg.AssetId, outLeg.Amount, v.Value.Value, 0m, v.Value.Source, "Bezahlen mit Krypto (Veräußerung zum Marktwert)");
        }

        // ---- Einkünfte ----

        private void ProcessIncome(Transaction tx)
        {
            foreach (var leg in tx.Legs.Where(l => !l.IsFee && l.Direction == LegDirection.In && IsTracked(l.AssetId)))
            {
                if (IsBlocked(tx, leg.AssetId)) continue;
                var time = tx.TimestampUtc;

                if (tx.Type == TransactionType.Mining)
                    Finding(FindingKind.MiningHint, "Mining oder Validator-Betrieb erkannt: möglicherweise gewerblich, außerhalb des Umfangs der App.", tx, leg.AssetId);

                var withoutConsideration = tx.Type == TransactionType.Airdrop && _settings.Airdrops == AirdropTreatment.WithoutConsideration;
                (decimal Value, string Source)? v = withoutConsideration ? (0m, "Airdrop ohne Gegenleistung") : ValueOf(leg.AssetId, leg.Amount, time);
                if (v is null) { MissingPrice(tx, leg.AssetId, $"{tx.Type}-Zufluss"); continue; }

                var regime = RegimeOf(leg.AssetId, time);
                var kind = tx.Type switch
                {
                    TransactionType.Staking => IncomeKind.Staking,
                    TransactionType.Lending => IncomeKind.Lending,
                    TransactionType.Interest => IncomeKind.Interest,
                    TransactionType.Mining => IncomeKind.Mining,
                    TransactionType.Airdrop when withoutConsideration => IncomeKind.AirdropWithoutConsideration,
                    TransactionType.Airdrop => IncomeKind.AirdropWithConsideration,
                    _ => IncomeKind.Bonus,
                };
                _incomes.Add(new IncomeRecord(tx.Id, tx.ExternalId, time, BerlinTime.YearOf(time), tx.AccountId, leg.AssetId,
                    leg.Amount, v.Value.Value, kind, v.Value.Source, CountsAsIncome: !withoutConsideration, regime));

                // Zufluss = Anschaffung: Lot mit dem Marktwert als Kosten, Frist beginnt mit dem Zufluss.
                Acquire(tx, leg.AssetId, leg.Amount, v.Value.Value, withoutConsideration ? LotKind.Airdrop : LotKind.Income, time);
            }
        }

        // ---- Futures ----

        private void ProcessFutures(Transaction tx)
        {
            var legs = tx.Legs.Where(l => IsTracked(l.AssetId)).ToList();
            if (legs.Count == 0) return; // EUR-Gebühren (z. B. Kartengebühr) gehören nicht zu den Futures

            var kind = tx.Type switch
            {
                TransactionType.FuturesPnL => FuturesKind.RealizedPnl,
                TransactionType.FundingFee => FuturesKind.FundingFee,
                TransactionType.Liquidation => FuturesKind.Liquidation,
                _ => FuturesKind.TradingFee,
            };

            // Ein Kurs je Buchung und Coin (Hauptbein und Gebühr desselben Coins).
            foreach (var group in legs.GroupBy(l => l.AssetId))
            {
                var main = group.Where(l => !l.IsFee).Sum(l => l.Direction == LegDirection.In ? l.Amount : -l.Amount);
                var fee = group.Where(l => l.IsFee).Sum(l => l.Amount);
                var rate = ValueOf(group.Key, 1m, tx.TimestampUtc);
                if (rate is null)
                {
                    // Sammelmeldung je Coin statt tausender Einzelmeldungen (Funding fällt mehrmals täglich an).
                    var old = _missingFuturesRates.GetValueOrDefault(group.Key);
                    _missingFuturesRates[group.Key] = old is null
                        ? new MissingRange(1, tx.TimestampUtc, tx.TimestampUtc)
                        : new MissingRange(old.Count + 1, old.From, tx.TimestampUtc);
                    continue;
                }

                // Typ "Fee" hat das Gebührenbein als Hauptbein; deshalb bei TradingFee der Betrag als Kosten (negativ) buchen.
                var amountEur = main * rate.Value.Value;
                var feeEur = fee * rate.Value.Value;
                _futures.Add(new FuturesRecord(tx.Id, tx.ExternalId, tx.TimestampUtc, BerlinTime.YearOf(tx.TimestampUtc), tx.AccountId, group.Key,
                    kind, main, amountEur, feeEur, rate.Value.Source, tx.Note));
            }
        }

        // ---- Transfers ----

        private void ProcessTransferOut(Transaction tx)
        {
            var main = tx.Legs.FirstOrDefault(l => !l.IsFee);
            if (main is null || !IsTracked(main.AssetId)) return;
            if (IsBlocked(tx, main.AssetId)) return;
            if (!_matchByOut.TryGetValue(tx.Id, out var match)) return; // Befund wurde bereits erzeugt; Lots bleiben

            var asset = main.AssetId;
            var outTotal = match.OutQuantity;
            var inQty = match.InQuantity;
            var pool = Pool(tx.AccountId, asset);
            var held = pool.Sum(l => l.Remaining);
            if (held + Dust < outTotal)
            {
                NegativeHolding(tx, asset, outTotal, held);
                return;
            }

            var slices = _strategy.Consume(Ordered(pool), Math.Min(outTotal, held));
            var pieces = new List<TransitPiece>();
            var movedLeft = inQty;
            var feeQtyTotal = outTotal - inQty;

            for (var i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                var last = i == slices.Count - 1;
                var moved = last ? movedLeft : s.Quantity * inQty / outTotal;
                var feePart = s.Quantity - moved;
                var movedCost = s.Quantity == 0 ? 0m : last ? s.CostEur * moved / s.Quantity : s.CostEur * moved / s.Quantity;
                movedLeft -= moved;
                pieces.Add(new TransitPiece(s.Lot.AcquiredAtUtc, moved, movedCost, s.Lot.Regime, LotKind.Transfer, s.Lot.SourceTransactionId, s.Lot.SourceExternalId));

                if (feePart > 0 && _settings.NetworkFees == NetworkFeeTreatment.DisposalAtMarketValue)
                {
                    var v = ValueOf(asset, feePart, tx.TimestampUtc);
                    if (v is null) { MissingPrice(tx, asset, "Netzwerkgebühr als Veräußerung"); Block(asset); return; }
                    var feeCost = s.CostEur - movedCost;
                    _disposals.Add(MakeDisposal(tx, s.Lot, feePart, v.Value.Value, feeCost, 0m, v.Value.Source, "Netzwerkgebühr (Option b: Veräußerung zum Marktwert)"));
                }
            }

            if (feeQtyTotal > 0)
            {
                var value = _settings.NetworkFees == NetworkFeeTreatment.DisposalAtMarketValue ? ValueOf(asset, feeQtyTotal, tx.TimestampUtc)?.Value : null;
                _networkFees.Add(new NetworkFeeRecord(tx.Id, tx.TimestampUtc, BerlinTime.YearOf(tx.TimestampUtc), asset, feeQtyTotal, value,
                    _settings.NetworkFees == NetworkFeeTreatment.NeutralWithoutProceeds ? TaxTreatment.NetworkFeeNeutral : TaxTreatment.Section23Taxable));
            }

            _transfers.Add(new TransferRecord(match.Out.Id, match.In.Id, asset, match.Out.AccountId, match.In.AccountId, outTotal, inQty, feeQtyTotal, match.Confidence));
            _transit[match.Out.Id] = pieces;
            // Falls der Eingang vor der Auszahlung einsortiert wurde (gleicher Zeitstempel), wurde er zurückgestellt.
            if (_pendingIn.Remove(match.In.Id, out var pending)) Materialize(pending, match);
        }

        private readonly Dictionary<Guid, Transaction> _pendingIn = [];

        private sealed record MissingRange(int Count, DateTimeOffset From, DateTimeOffset To);

        private readonly Dictionary<Guid, MissingRange> _missingFuturesRates = [];

        private void ProcessTransferIn(Transaction tx)
        {
            var main = tx.Legs.FirstOrDefault(l => !l.IsFee);
            if (main is null || !IsTracked(main.AssetId)) return;
            if (IsBlocked(tx, main.AssetId)) return;
            if (!_matchByIn.TryGetValue(tx.Id, out var match)) return; // unmatched: Befund bereits erzeugt

            if (_transit.TryGetValue(match.Out.Id, out var pieces)) Materialize(tx, match, pieces);
            else _pendingIn[tx.Id] = tx;
        }

        private void Materialize(Transaction inTx, TransferMatch match) => Materialize(inTx, match, _transit[match.Out.Id]);

        private void Materialize(Transaction inTx, TransferMatch match, List<TransitPiece> pieces)
        {
            foreach (var p in pieces.Where(p => p.Quantity > 0))
                AddLot(inTx.AccountId, match.AssetId, p.AcquiredAtUtc, p.Quantity, p.CostEur, p.Regime, p.Kind, inTx.Id, inTx.ExternalId);
            _transit.Remove(match.Out.Id);
        }

        // ---- Lots ----

        private List<LotState> Pool(Guid account, Guid asset)
        {
            if (!_lots.TryGetValue((account, asset), out var list)) _lots[(account, asset)] = list = [];
            return list;
        }

        /// <summary>Verbrauchsreihenfolge: nach Anschaffungsdatum (FIFO), optional Neubestand zuerst.</summary>
        private IReadOnlyList<LotState> Ordered(List<LotState> pool)
        {
            var q = pool.Where(l => l.Remaining > 0);
            q = _settings.RegimeOrder == RegimeOrder.NeuFirst
                ? q.OrderByDescending(l => l.Regime == TaxRegime.Neu).ThenBy(l => l.AcquiredAtUtc).ThenBy(l => l.Sequence)
                : q.OrderBy(l => l.AcquiredAtUtc).ThenBy(l => l.Sequence);
            return q.ToList();
        }

        private void Acquire(Transaction tx, Guid asset, decimal qty, decimal costEur, LotKind kind, DateTimeOffset at) =>
            AddLot(tx.AccountId, asset, at, qty, costEur, RegimeOf(asset, at), kind, tx.Id, tx.ExternalId);

        private void AddLot(Guid account, Guid asset, DateTimeOffset at, decimal qty, decimal cost, TaxRegime regime, LotKind kind, Guid txId, string? externalId)
        {
            var seq = ++_sequence;
            Pool(account, asset).Add(new LotState
            {
                Id = Id("lot", txId, asset, account, seq), Sequence = seq, AccountId = account, AssetId = asset,
                AcquiredAtUtc = at, AcquiredBerlinDate = BerlinTime.DateOf(at), Quantity = qty, CostEur = cost,
                Regime = regime, Kind = kind, SourceTransactionId = txId, SourceExternalId = externalId,
                Remaining = qty, RemainingCost = cost,
            });
        }

        /// <summary>Verbraucht Lots und erzeugt je Lot einen Disposal-Datensatz. Bei fehlendem Bestand wird nichts gebucht und das Asset gestoppt.</summary>
        private bool Dispose(Transaction tx, Guid account, Guid asset, decimal qty, decimal proceedsEur, decimal feeEur, string priceSource, string? note)
        {
            var pool = Pool(account, asset);
            var held = pool.Sum(l => l.Remaining);
            if (held + Dust < qty)
            {
                NegativeHolding(tx, asset, qty, held);
                return false;
            }

            var slices = _strategy.Consume(Ordered(pool), Math.Min(qty, held));
            var proceedsLeft = proceedsEur;
            var feeLeft = feeEur;
            var qtyLeft = qty;

            for (var i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                var last = i == slices.Count - 1;
                var proceeds = last ? proceedsLeft : proceedsEur * s.Quantity / qty;
                var fee = last ? feeLeft : feeEur * s.Quantity / qty;
                proceedsLeft -= proceeds;
                feeLeft -= fee;
                qtyLeft -= s.Quantity;
                _disposals.Add(MakeDisposal(tx, s.Lot, s.Quantity, proceeds, s.CostEur, fee, priceSource, note));
            }

            return true;
        }

        private DisposalRecord MakeDisposal(Transaction tx, LotState lot, decimal qty, decimal proceeds, decimal cost, decimal fee, string priceSource, string? note)
        {
            var saleDate = BerlinTime.DateOf(tx.TimestampUtc);
            var years = (int)input.Law.Get("HoldingPeriodYears", saleDate.Year);
            var free = HoldingPeriod.TaxFreeFrom(lot.AcquiredBerlinDate, years);
            var within = saleDate < free;

            var treatment = lot.Regime == TaxRegime.Neu && _settings.ApplyReform2027 ? TaxTreatment.ReformCapital
                : within ? TaxTreatment.Section23Taxable : TaxTreatment.TaxFreeAfterHoldingPeriod;

            return new DisposalRecord(
                Id("disposal", tx.Id, lot.Id, qty, _disposals.Count), tx.Id, tx.ExternalId, tx.TimestampUtc, saleDate.Year,
                tx.AccountId, lot.AssetId, lot.Id, qty, proceeds, cost, fee, proceeds - cost - fee,
                lot.AcquiredAtUtc, HoldingPeriod.Days(lot.AcquiredBerlinDate, saleDate), free, within, lot.Regime, treatment, priceSource, note);
        }

        private static LotRecord ToRecord(LotState l)
        {
            var free = HoldingPeriod.TaxFreeFrom(l.AcquiredBerlinDate);
            return new LotRecord(l.Id, l.Sequence, l.AccountId, l.AssetId, l.AcquiredAtUtc, l.AcquiredBerlinDate, l.Quantity, l.Remaining,
                l.CostEur, l.RemainingCost, l.Regime, free, l.Kind, l.SourceTransactionId, l.SourceExternalId);
        }

        private void NegativeHolding(Transaction tx, Guid asset, decimal needed, decimal held)
        {
            Finding(FindingKind.NegativeHolding,
                $"Negativer Bestand bei {Sym(asset)}: benötigt {needed:0.##########}, vorhanden {held:0.##########} ({Describe(tx)}). " +
                "Vermutlich fehlt die Historie (Export, Transfer). Die Berechnung für dieses Asset wird gestoppt, es wird nicht mit 0 EUR Kosten weitergerechnet.", tx, asset);
            Block(asset);
        }

        private void MissingPrice(Transaction tx, Guid asset, string purpose) =>
            Finding(FindingKind.MissingPrice, $"EUR-Kurs für {Sym(asset)} fehlt ({purpose}, {tx.TimestampUtc:yyyy-MM-dd HH:mm}Z). Es wird nie 0 EUR angenommen.", tx, asset);
    }
}
