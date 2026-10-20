using System.Security.Cryptography;
using System.Text;

namespace KryptoSteuer.Importers.Bitget;

/// <summary>
/// Signatur der Bitget-REST-API: Base64(HMAC-SHA256(secret, timestamp + METHOD + requestPath + ["?" + query] + body)).
/// Zeitstempel in Millisekunden (Unix). Header: ACCESS-KEY, ACCESS-SIGN, ACCESS-TIMESTAMP, ACCESS-PASSPHRASE.
/// </summary>
public static class BitgetSigner
{
    public static string Sign(string secret, long timestampMs, string method, string requestPath, string queryString, string body)
    {
        var query = string.IsNullOrEmpty(queryString) ? string.Empty : "?" + queryString.TrimStart('?');
        var message = timestampMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + method.ToUpperInvariant() + requestPath + query + body;
        return Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message)));
    }
}
