using System.Text.Json;
using KryptoSteuer.Importers.Bitget;

namespace KryptoSteuer.Importers.Tests.Bitget;

/// <summary>Skriptbarer Ersatz für die Bitget-API: Antworten je Pfad, alle Aufrufe werden protokolliert.</summary>
internal sealed class FakeBitgetClient : IBitgetClient
{
    public sealed record Call(string Path, IReadOnlyDictionary<string, string> Query, bool Signed);

    public List<Call> Calls { get; } = [];
    public Dictionary<string, Func<IReadOnlyDictionary<string, string>, string>> Routes { get; } = [];

    public FakeBitgetClient Route(string path, Func<IReadOnlyDictionary<string, string>, string> handler)
    {
        Routes[path] = handler;
        return this;
    }

    public FakeBitgetClient Route(string path, string dataJson) => Route(path, _ => dataJson);

    public Task<JsonElement> GetAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken) =>
        Send(path, query, signed: true);

    public Task<JsonElement> GetPublicAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken) =>
        Send(path, query, signed: false);

    private Task<JsonElement> Send(string path, IReadOnlyList<KeyValuePair<string, string>> query, bool signed)
    {
        var q = query.ToDictionary(kv => kv.Key, kv => kv.Value);
        Calls.Add(new Call(path, q, signed));
        if (!Routes.TryGetValue(path, out var route))
            throw new BitgetApiException("40404", $"Pfad nicht gefunden: {path}");
        return Task.FromResult(JsonDocument.Parse(route(q)).RootElement.Clone());
    }

    public IEnumerable<Call> CallsTo(string path) => Calls.Where(c => c.Path == path);
}
