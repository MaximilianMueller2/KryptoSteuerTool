using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace KryptoSteuer.Importers.Bitget;

public sealed record BitgetCredentials(string ApiKey, string Secret, string Passphrase);

/// <summary>Fehler der Bitget-API (HTTP-Status oder Fehlercode ungleich "00000").</summary>
public sealed class BitgetApiException(string code, string message, HttpStatusCode? status = null)
    : Exception($"Bitget {code}: {message}")
{
    public string Code { get; } = code;
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Lesender Zugriff auf die Bitget-API. Liefert das <c>data</c>-Element der Antwort.</summary>
public interface IBitgetClient
{
    /// <summary>Signierter GET-Aufruf; Parameter in der übergebenen Reihenfolge.</summary>
    Task<JsonElement> GetAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken);

    /// <summary>Öffentlicher GET-Aufruf ohne Signatur.</summary>
    Task<JsonElement> GetPublicAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken);
}

/// <summary>
/// HTTP-Client mit Signatur, Zeitabgleich über den Server-Time-Endpunkt und Retry mit exponentiellem Backoff
/// bei HTTP 429 und 5xx (Rate-Limits). Es werden ausschließlich GET-Aufrufe gesendet.
/// </summary>
public sealed class BitgetHttpClient(
    HttpClient http,
    BitgetCredentials credentials,
    TimeProvider clock,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IBitgetClient
{
    public const string ServerTimePath = "/api/v2/public/time";
    public const int MaxAttempts = 5;
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private long? _offsetMs;

    public Task<JsonElement> GetPublicAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken) =>
        SendAsync(path, query, signed: false, cancellationToken);

    public Task<JsonElement> GetAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken) =>
        SendAsync(path, query, signed: true, cancellationToken);

    private async Task<JsonElement> SendAsync(
        string path, IReadOnlyList<KeyValuePair<string, string>> query, bool signed, CancellationToken cancellationToken)
    {
        var queryString = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path + (queryString.Length > 0 ? "?" + queryString : ""));
            request.Headers.TryAddWithoutValidation("locale", "en-US");

            if (signed)
            {
                var timestamp = await CurrentTimestampAsync(cancellationToken);
                request.Headers.TryAddWithoutValidation("ACCESS-KEY", credentials.ApiKey);
                request.Headers.TryAddWithoutValidation("ACCESS-PASSPHRASE", credentials.Passphrase);
                request.Headers.TryAddWithoutValidation("ACCESS-TIMESTAMP", timestamp.ToString(CultureInfo.InvariantCulture));
                request.Headers.TryAddWithoutValidation(
                    "ACCESS-SIGN", BitgetSigner.Sign(credentials.Secret, timestamp, "GET", path, queryString, string.Empty));
            }

            using var response = await http.SendAsync(request, cancellationToken);
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (retryable && attempt < MaxAttempts)
            {
                await _delay(Backoff(attempt, response), cancellationToken);
                continue;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new BitgetApiException(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                    Truncate(text), response.StatusCode);

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
            if (code != "00000")
                throw new BitgetApiException(code, root.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "", response.StatusCode);

            return root.TryGetProperty("data", out var data) ? data.Clone() : default;
        }
    }

    private async Task<long> CurrentTimestampAsync(CancellationToken cancellationToken)
    {
        if (_offsetMs is null)
        {
            // Zeitabgleich: Server-Zeit minus lokale Zeit, einmal je Client.
            var data = await SendAsync(ServerTimePath, [], signed: false, cancellationToken);
            var serverMs = long.Parse(data.GetProperty("serverTime").GetString()!, CultureInfo.InvariantCulture);
            _offsetMs = serverMs - clock.GetUtcNow().ToUnixTimeMilliseconds();
        }

        return clock.GetUtcNow().ToUnixTimeMilliseconds() + _offsetMs.Value;
    }

    private static TimeSpan Backoff(int attempt, HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter)
            return retryAfter;
        return TimeSpan.FromTicks(BaseDelay.Ticks * (1L << (attempt - 1)));
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
