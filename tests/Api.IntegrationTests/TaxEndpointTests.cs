using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace KryptoSteuer.Api.IntegrationTests;

/// <summary>Import → Kurse → Berechnungslauf → Einzelaufstellung über HTTP gegen echtes PostgreSQL.</summary>
public sealed class TaxEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new ApiFactory(_postgres.GetConnectionString(), migrate: true);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task ImportTradeRepublic()
    {
        var created = await _client.PostAsJsonAsync("/api/accounts",
            new { name = "Trade Republic", type = "Broker", platform = "TradeRepublic", country = "DE" });
        var accountId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "traderepublic", "traderepublic-sample.csv");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(File.ReadAllBytes(path)), "file", "traderepublic-sample.csv");
        form.Add(new StringContent(accountId.ToString()), "accountId");
        var upload = await _client.PostAsync("/api/imports", form);
        upload.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var importId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await _client.PostAsync($"/api/imports/{importId}/commit", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Run_is_stored_and_second_run_reports_unchanged_input()
    {
        await ImportTradeRepublic();

        var first = await _client.PostAsJsonAsync("/api/tax/runs", new { year = 2025 });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("counts").GetProperty("transactions").GetInt32().Should().BeGreaterThan(0);
        var hash = firstBody.GetProperty("inputHash").GetString();
        hash.Should().HaveLength(64);

        var second = await (await _client.PostAsJsonAsync("/api/tax/runs", new { year = 2025 })).Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("inputHash").GetString().Should().Be(hash);
        second.GetProperty("inputUnchanged").GetBoolean().Should().BeTrue();
        second.GetProperty("diffToPrevious").GetArrayLength().Should().Be(0);

        var id = firstBody.GetProperty("id").GetGuid();
        (await _client.GetAsync($"/api/tax/runs/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/tax/runs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_year_is_rejected()
    {
        (await _client.PostAsJsonAsync("/api/tax/runs", new { year = 1900 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Disposals_csv_has_bom_and_header()
    {
        await ImportTradeRepublic();
        var response = await _client.GetAsync("/api/tax/years/2025/disposals.csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        (await _client.GetAsync("/api/tax/years/2025/disposals")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ecb_upload_and_manual_price_are_stored()
    {
        await ImportTradeRepublic();
        var csv = "Date,USD,JPY,\n2025-01-03,1.04,160,\n2025-01-02,1.03,159,\n";
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "eurofxref-hist.csv");
        var upload = await _client.PostAsync("/api/prices/ecb", form);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rates").GetInt32().Should().Be(2);

        using var bad = new MultipartFormDataContent();
        bad.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("foo,bar\n1,2\n")), "file", "x.csv");
        (await _client.PostAsync("/api/prices/ecb", bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var manual = await _client.PutAsJsonAsync("/api/prices", new { symbol = "BTC", at = "2025-02-01T00:00:00Z", eurPrice = 90000.5, source = "Handeingabe" });
        manual.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await (await _client.GetAsync("/api/prices")).Content.ReadFromJsonAsync<JsonElement>();
        list.GetArrayLength().Should().BeGreaterThan(0);
    }

    private async Task<JsonElement> OpenReview(string kind)
    {
        var review = await (await _client.GetAsync("/api/review")).Content.ReadFromJsonAsync<JsonElement>();
        return review.GetProperty("open").EnumerateArray().First(i => i.GetProperty("kind").GetString() == kind);
    }

    private async Task<int> OpenCount(string kind)
    {
        var review = await (await _client.GetAsync("/api/review")).Content.ReadFromJsonAsync<JsonElement>();
        return review.GetProperty("open").EnumerateArray().Count(i => i.GetProperty("kind").GetString() == kind);
    }

    [Fact]
    public async Task Own_transfer_decision_resolves_the_finding_is_audited_and_can_be_revoked()
    {
        await ImportTradeRepublic();
        var item = await OpenReview("UnmatchedTransferOut");
        item.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().Contain("transfer-to-own-account");
        var txId = item.GetProperty("transactionId").GetGuid();
        var before = (await (await _client.PostAsJsonAsync("/api/tax/runs", new { year = 2026 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inputHash").GetString();

        var decided = await _client.PutAsJsonAsync($"/api/review/transfers/{txId}/own-account", new { newAccountName = "Bitget Wallet", note = "SOL auf Bitget Wallet" });
        decided.StatusCode.Should().Be(HttpStatusCode.OK);

        (await OpenCount("UnmatchedTransferOut")).Should().Be(0);
        var run = await (await _client.PostAsJsonAsync("/api/tax/runs", new { year = 2026 })).Content.ReadFromJsonAsync<JsonElement>();
        run.GetProperty("counts").GetProperty("transfers").GetInt32().Should().Be(1);
        run.GetProperty("inputHash").GetString().Should().NotBe(before);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KryptoSteuer.Infrastructure.Persistence.AppDbContext>();
            db.AuditLog.Count(a => a.EntityType == "ReviewDecision").Should().Be(1);
            db.Accounts.Count(a => a.Name == "Bitget Wallet").Should().Be(1);
        }

        (await _client.DeleteAsync($"/api/review/decisions/{txId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"/api/review/decisions/{txId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OpenCount("UnmatchedTransferOut")).Should().Be(1);
    }

    [Fact]
    public async Task Review_decisions_validate_their_input()
    {
        await ImportTradeRepublic();
        var txId = (await OpenReview("UnmatchedTransferOut")).GetProperty("transactionId").GetGuid();

        (await _client.PutAsJsonAsync($"/api/review/transfers/{txId}/own-account", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PutAsJsonAsync($"/api/review/transfers/{txId}/own-account", new { targetAccountId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PutAsJsonAsync($"/api/review/transfers/{Guid.NewGuid()}/own-account", new { newAccountName = "X" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // eine normale Buchung ist keine Auszahlung und hat keinen Prüfstatus
        var review = await OpenReview("TransactionNeedsReview");
        var reviewTx = review.GetProperty("transactionId").GetGuid();
        (await _client.PutAsJsonAsync($"/api/review/transfers/{reviewTx}/own-account", new { newAccountName = "X" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Acknowledging_a_review_transaction_removes_it_from_the_open_list()
    {
        await ImportTradeRepublic();
        var open = await OpenCount("TransactionNeedsReview");
        open.Should().BeGreaterThan(0);
        var txId = (await OpenReview("TransactionNeedsReview")).GetProperty("transactionId").GetGuid();

        (await _client.PutAsJsonAsync($"/api/review/transactions/{txId}/acknowledge", new { note = "Wertpapier, nicht Krypto" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await OpenCount("TransactionNeedsReview")).Should().Be(open - 1);
        (await _client.PutAsJsonAsync($"/api/review/transactions/{Guid.NewGuid()}/acknowledge", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<ClosedXML.Excel.XLWorkbook> Workbook(string query = "", int year = 2024)
    {
        var response = await _client.GetAsync($"/api/reports/{year}/xlsx{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return new ClosedXML.Excel.XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task Report_pdf_xlsx_and_csv_agree_to_the_cent_and_flag_drafts()
    {
        await ImportTradeRepublic();

        var pdf = await _client.GetAsync("/api/reports/2024/pdf?name=Erika%20Muster");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var pdfBytes = await pdf.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(pdfBytes, 0, 5).Should().Be("%PDF-");
        pdfBytes.Length.Should().BeGreaterThan(5000);

        using var wb = await Workbook("?name=Erika%20Muster");
        wb.Worksheets.Select(w => w.Name).Should().Equal("Deckblatt", "Zusammenfassung", "Veräußerungen", "Laufende Einkünfte", "Bestand 31.12.", "Korrekturen", "Quellen");
        var cover = wb.Worksheet("Deckblatt");
        cover.CellsUsed().Select(c => c.GetString()).Should().Contain(s => s.StartsWith("ENTWURF", StringComparison.Ordinal));
        cover.CellsUsed().Select(c => c.GetString()).Should().Contain("Erika Muster");

        // Summen im Blatt = Summe der gerundeten Zeilen = Einzelaufstellung der API
        var sheet = wb.Worksheet("Veräußerungen");
        var rows = sheet.RowsUsed().Skip(1).Where(r => r.Cell(1).GetString().Length > 0 && !r.Cell(1).GetString().StartsWith("Summe", StringComparison.Ordinal)).ToList();
        rows.Should().NotBeEmpty();
        var taxableGain = rows.Where(r => r.Cell(11).GetString() == "ja").Sum(r => r.Cell(10).GetValue<decimal>());
        var api = await (await _client.GetAsync("/api/tax/years/2024/disposals")).Content.ReadFromJsonAsync<JsonElement>();
        taxableGain.Should().Be(api.GetProperty("taxableNetEur").GetDecimal());
        var summarySo = wb.Worksheet("Zusammenfassung").CellsUsed().First(c => c.GetString().StartsWith("Gewinn/Verlust", StringComparison.Ordinal)).CellRight().GetValue<decimal>();
        summarySo.Should().Be(taxableGain);

        var csv = Encoding.UTF8.GetString(await (await _client.GetAsync("/api/reports/2024/csv")).Content.ReadAsByteArrayAsync());
        csv.Should().Contain("Summe steuerpflichtig (Netto);;;;;;;;;" + taxableGain.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("de-DE")));
        csv.Split('\n').Count(l => l.StartsWith("SOL;", StringComparison.Ordinal)).Should().Be(rows.Count(r => r.Cell(1).GetString() == "SOL"));
    }

    [Fact]
    public async Task Report_becomes_final_when_all_findings_are_resolved_and_lists_the_corrections()
    {
        await ImportTradeRepublic();
        var review = await (await _client.GetAsync("/api/review")).Content.ReadFromJsonAsync<JsonElement>();
        var resolved = review.GetProperty("open").GetArrayLength();
        foreach (var item in review.GetProperty("open").EnumerateArray())
        {
            var tx = item.GetProperty("transactionId").GetGuid();
            if (item.GetProperty("kind").GetString() == "TransactionNeedsReview")
                await _client.PutAsJsonAsync($"/api/review/transactions/{tx}/acknowledge", new { note = "Wertpapier" });
            else
                await _client.PutAsJsonAsync($"/api/review/transfers/{tx}/own-account", new { newAccountName = "Bitget Wallet", note = "SOL Transfer" });
        }

        // Der Transfer liegt im Jahr 2026: der Bestand zum 31.12.2026 liegt im neuen Wallet-Konto.
        using var wb = await Workbook(year: 2026);
        wb.Worksheet("Deckblatt").CellsUsed().Select(c => c.GetString()).Should().Contain(s => s.StartsWith("final", StringComparison.Ordinal));
        var audit = wb.Worksheet("Korrekturen");
        audit.RowsUsed().Count().Should().Be(1 + resolved);
        wb.Worksheet("Quellen").RowsUsed().Count().Should().Be(2);
        wb.Worksheet("Bestand 31.12.").RowsUsed().Skip(1).Select(r => r.Cell(1).GetString()).Should().Contain("Bitget Wallet");
    }

    [Fact]
    public async Task Report_rejects_invalid_years()
    {
        (await _client.GetAsync("/api/reports/1900/pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetAsync("/api/reports/1900/xlsx")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetAsync("/api/reports/1900/csv")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Holdings_lists_open_lots_per_account_and_asset()
    {
        await ImportTradeRepublic();
        var holdings = await (await _client.GetAsync("/api/tax/holdings")).Content.ReadFromJsonAsync<JsonElement>();

        var rows = holdings.EnumerateArray().ToList();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.GetProperty("account").GetString() == "Trade Republic" && r.GetProperty("quantity").GetDecimal() > 0 && r.GetProperty("costEur").GetDecimal() > 0);
        rows.Select(r => r.GetProperty("asset").GetString()).Should().OnlyHaveUniqueItems().And.Contain("XRP");
    }
}
