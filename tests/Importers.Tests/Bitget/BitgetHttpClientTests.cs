using System.Net;
using System.Text;
using KryptoSteuer.Importers.Bitget;
using Microsoft.Extensions.Time.Testing;

namespace KryptoSteuer.Importers.Tests.Bitget;

public class BitgetHttpClientTests
{
    private sealed class ScriptedHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request, Requests.Count));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (BitgetHttpClient Client, ScriptedHandler Handler, List<TimeSpan> Delays) Create(
        Func<HttpRequestMessage, int, HttpResponseMessage> respond, long serverTimeMs = 1_700_000_005_000)
    {
        var handler = new ScriptedHandler((req, n) =>
            req.RequestUri!.AbsolutePath == BitgetHttpClient.ServerTimePath
                ? Json($"{{\"code\":\"00000\",\"msg\":\"success\",\"data\":{{\"serverTime\":\"{serverTimeMs}\"}}}}")
                : respond(req, n));
        var delays = new List<TimeSpan>();
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var client = new BitgetHttpClient(http, new BitgetCredentials("key", "test-secret", "pass"), clock,
            (d, _) => { delays.Add(d); return Task.CompletedTask; });
        return (client, handler, delays);
    }

    [Fact]
    public async Task Signierter_Aufruf_sendet_Header_und_nutzt_die_Server_Zeit()
    {
        var (client, handler, _) = Create((_, _) => Json("{\"code\":\"00000\",\"msg\":\"success\",\"data\":[1]}"));

        var data = await client.GetAsync("/api/v2/spot/account/assets",
            [new("coin", "BTC"), new("limit", "100")], CancellationToken.None);

        data.GetArrayLength().Should().Be(1);
        var request = handler.Requests.Single(r => r.RequestUri!.AbsolutePath != BitgetHttpClient.ServerTimePath);
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.Query.Should().Be("?coin=BTC&limit=100");
        request.Headers.GetValues("ACCESS-KEY").Single().Should().Be("key");
        request.Headers.GetValues("ACCESS-PASSPHRASE").Single().Should().Be("pass");
        // lokale Zeit 1_700_000_000_000 + Versatz 5 s zur Server-Zeit
        request.Headers.GetValues("ACCESS-TIMESTAMP").Single().Should().Be("1700000005000");
        request.Headers.GetValues("ACCESS-SIGN").Single().Should().Be(
            BitgetSigner.Sign("test-secret", 1_700_000_005_000, "GET", "/api/v2/spot/account/assets", "coin=BTC&limit=100", ""));
    }

    [Fact]
    public async Task Oeffentliche_Aufrufe_senden_keine_Zugangsdaten()
    {
        var (client, handler, _) = Create((_, _) => Json("{\"code\":\"00000\",\"msg\":\"success\",\"data\":[]}"));

        await client.GetPublicAsync("/api/v2/spot/public/symbols", [], CancellationToken.None);

        var request = handler.Requests.Single();
        request.Headers.Contains("ACCESS-KEY").Should().BeFalse();
        request.Headers.Contains("ACCESS-SIGN").Should().BeFalse();
    }

    [Fact]
    public async Task Rate_Limit_429_wird_mit_exponentiellem_Backoff_wiederholt()
    {
        var (client, handler, delays) = Create((_, n) => n < 4
            ? Json("{}", HttpStatusCode.TooManyRequests)
            : Json("{\"code\":\"00000\",\"msg\":\"success\",\"data\":[]}"));

        await client.GetPublicAsync("/x", [], CancellationToken.None);

        handler.Requests.Should().HaveCount(4);
        delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Server_Fehler_5xx_werden_wiederholt_und_nach_dem_Limit_gemeldet()
    {
        var (client, handler, delays) = Create((_, _) => Json("kaputt", HttpStatusCode.BadGateway));

        var act = () => client.GetPublicAsync("/x", [], CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<BitgetApiException>()).Which;
        ex.Status.Should().Be(HttpStatusCode.BadGateway);
        handler.Requests.Should().HaveCount(BitgetHttpClient.MaxAttempts);
        delays.Should().HaveCount(BitgetHttpClient.MaxAttempts - 1);
    }

    [Fact]
    public async Task Fehlercode_ungleich_00000_wird_als_BitgetApiException_gemeldet()
    {
        var (client, _, _) = Create((_, _) => Json("{\"code\":\"40009\",\"msg\":\"sign signature error\",\"data\":null}"));

        var act = () => client.GetAsync("/x", [], CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<BitgetApiException>()).Which;
        ex.Code.Should().Be("40009");
        ex.Message.Should().Contain("sign signature error");
    }

    [Fact]
    public async Task Client_Fehler_4xx_werden_nicht_wiederholt()
    {
        var (client, handler, delays) = Create((_, _) => Json("{\"code\":\"40014\",\"msg\":\"x\"}", HttpStatusCode.BadRequest));

        var act = () => client.GetAsync("/x", [], CancellationToken.None);

        await act.Should().ThrowAsync<BitgetApiException>();
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/x").Should().Be(1);
        delays.Should().BeEmpty();
    }

    [Fact]
    public async Task Retry_After_Header_wird_beachtet()
    {
        var (client, _, delays) = Create((_, n) =>
        {
            if (n > 1) return Json("{\"code\":\"00000\",\"msg\":\"success\",\"data\":[]}");
            var response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });

        await client.GetPublicAsync("/x", [], CancellationToken.None);

        delays.Should().Equal(TimeSpan.FromSeconds(7));
    }
}
