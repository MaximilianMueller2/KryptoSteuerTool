using System.IO.Compression;
using System.Text;

namespace KryptoSteuer.Api.Prices;

/// <summary>
/// Lädt die EZB-Referenzkurse (Standard: <c>eurofxref-hist.zip</c>, alternativ eine CSV-Datei) über die Konfiguration <c>Prices:EcbUrl</c>.
/// Die Standard-Adresse ist nicht gegen den Live-Dienst geprüft; ein Upload der Datei (<c>POST /api/prices/ecb</c>) geht immer.
/// </summary>
public sealed class EcbRatesClient(HttpClient http, IConfiguration configuration)
{
    public const string HttpClientName = "ecb";
    public const string DefaultUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist.zip";

    public async Task<string> FetchCsvAsync(CancellationToken cancellationToken)
    {
        var url = configuration["Prices:EcbUrl"] is { Length: > 0 } configured ? configured : DefaultUrl;
        var bytes = await http.GetByteArrayAsync(url, cancellationToken);
        return ExtractCsv(bytes);
    }

    /// <summary>Erkennt ZIP (Magic "PK") und liest die erste CSV-Datei; sonst gilt der Inhalt selbst als CSV.</summary>
    public static string ExtractCsv(byte[] bytes)
    {
        if (bytes.Length > 3 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Das ZIP enthält keine CSV-Datei.");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
    }
}
