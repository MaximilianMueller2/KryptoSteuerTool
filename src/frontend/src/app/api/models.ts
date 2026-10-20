/** Typen der API (siehe /openapi/v1.json); Enums kommen als Zeichenketten. */

export type AccountType = 'Exchange' | 'Wallet' | 'Broker';

export interface Account {
  id: string;
  name: string;
  type: AccountType;
  platform: string;
  country: string | null;
  subAccount: string | null;
}

export interface CreateAccountRequest {
  name: string;
  type: AccountType;
  platform: string;
  country?: string | null;
  subAccount?: string | null;
}

export interface ImportError {
  lineNumber: number | null;
  reason: string;
}

export interface ImportReport {
  rowsRead: number;
  new: number;
  duplicates: number;
  needsReview: number;
  errors: ImportError[];
  unknownTypes: string[];
  warnings: string[];
}

export interface ImportRun {
  id: string;
  source: string;
  fileName: string;
  accountId: string;
  status: 'Preview' | 'Committed' | string;
  createdAtUtc: string;
  committedAtUtc: string | null;
  report: ImportReport;
}

export interface Finding {
  kind: string;
  message: string;
  transactionId: string | null;
  assetId: string | null;
  accountId: string | null;
  taxYear: number | null;
}

export interface Section23 {
  proceedsEur: number;
  costEur: number;
  feesEur: number;
  gainsEur: number;
  lossesEur: number;
  netEur: number;
  freigrenzeEur: number;
  belowFreigrenze: boolean;
  lossCarryInEur: number;
  lossUsedEur: number;
  taxableEur: number;
  lossCarryOutEur: number;
  taxFreeAfterPeriodNetEur: number;
}

export interface Section22 {
  incomeEur: number;
  expensesEur: number;
  netEur: number;
  freigrenzeEur: number;
  belowFreigrenze: boolean;
  taxableEur: number;
}

export interface Futures {
  realizedGainsEur: number;
  realizedLossesEur: number;
  fundingNetEur: number;
  tradingFeesEur: number;
  liquidationEur: number;
  netEur: number;
  records: number;
}

export interface YearSummary {
  year: number;
  lawRuleSet: string;
  lawVersion: string;
  section23: Section23;
  section22: Section22;
  futures: Futures;
  openFindings: Finding[];
}

export interface TaxRunRequest {
  year: number;
  applyReform2027?: boolean;
  method?: 'Fifo' | 'Average';
  otherPrivateSalesGainEur?: number;
  lossCarryForwardInEur?: number;
}

export interface TaxRunResult {
  id: string;
  year: number;
  createdAtUtc: string;
  ruleSet: string;
  inputHash: string;
  inputUnchanged: boolean;
  counts: { lots: number; disposals: number; incomes: number; futuresRecords: number; transfers: number; findings: number; transactions: number };
  summary: YearSummary;
  findings: Finding[];
  diffToPrevious: { path: string; before: string | null; after: string | null }[] | null;
  isDraft: boolean;
}

export interface DisposalRow {
  asset: string;
  account: string;
  quantity: number;
  acquired: string;
  disposed: string;
  holdingDays: number;
  costEur: number;
  proceedsEur: number;
  feeEur: number;
  gainEur: number;
  taxable: boolean;
  treatment: string;
  priceSource: string;
  transactionId: string;
}

export interface DisposalReport {
  year: number;
  rows: DisposalRow[];
  taxableGainEur: number;
  taxableLossEur: number;
  taxableNetEur: number;
  taxFreeNetEur: number;
}

export interface ReviewItem {
  id: string;
  kind: string;
  message: string;
  transactionId: string | null;
  assetId: string | null;
  accountId: string | null;
  taxYear: number | null;
  actions: string[];
}

export interface DecisionView {
  transactionId: string;
  kind: 'TransferToOwnAccount' | 'Acknowledged';
  targetAccountId: string | null;
  note: string | null;
  createdAtUtc: string;
}

export interface ReviewList {
  open: ReviewItem[];
  decisions: DecisionView[];
}

export interface EcbImportResult {
  rates: number;
  assets: string[];
  pricesStored: number;
  warning: string | null;
}

export interface Holding {
  accountId: string;
  account: string;
  asset: string;
  quantity: number;
  costEur: number;
  lots: number;
  oldestAcquired: string;
  nextTaxFreeFrom: string;
}

export interface BitgetStatus {
  configured: boolean;
  accountType: string | null;
  warnings: string[];
  lastSyncAtUtc: string | null;
  lastSyncSummary: string | null;
}

export interface BalanceMismatch {
  symbol: string;
  exchangeBalance: number;
  computedHolding: number;
  difference: number;
}

export interface BitgetSyncResult {
  import: ImportReport;
  failedSections: string[];
  warnings: string[];
  balanceMismatches: BalanceMismatch[];
  balanceCheckSkipped: string | null;
}
