using KryptoSteuer.Importers.Bitget;

namespace KryptoSteuer.Api.Bitget;

/// <summary>Baut Bitget-Clients über <see cref="IHttpClientFactory"/> (Basisadresse und Timeout zentral konfiguriert).</summary>
public sealed class BitgetClientFactory(IHttpClientFactory httpClients, TimeProvider clock) : IBitgetClientFactory
{
    public const string HttpClientName = "bitget";

    public IBitgetClient Create(BitgetCredentials credentials) =>
        new BitgetHttpClient(httpClients.CreateClient(HttpClientName), credentials, clock);
}
