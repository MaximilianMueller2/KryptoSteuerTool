using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Application.Imports;

public sealed record ImportError(int? LineNumber, string Reason);

/// <summary>Zustand eines Importlaufs, den Importer beim Parsen und Normalisieren nutzen.</summary>
public sealed class ImportContext(
    Guid accountId, Guid importRunId, DateTimeOffset importedAtUtc, string fileHash, AssetCatalog assets,
    IReadOnlyDictionary<string, string>? options = null)
{
    private readonly List<string> _warnings = [];
    private readonly List<ImportError> _errors = [];
    private readonly SortedSet<string> _unknownTypes = new(StringComparer.OrdinalIgnoreCase);

    public Guid AccountId { get; } = accountId;
    public Guid ImportRunId { get; } = importRunId;
    public DateTimeOffset ImportedAtUtc { get; } = importedAtUtc;
    /// <summary>SHA-256 der Quelldatei.</summary>
    public string FileHash { get; } = fileHash;
    public AssetCatalog Assets { get; } = assets;
    public IReadOnlyDictionary<string, string> Options { get; } = options ?? new Dictionary<string, string>();

    /// <summary>Hinweise, die den Import nicht stoppen (z. B. abgeleitete Zeitzone, Lücke im Kontostand).</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Arbeitsspeicher für Importer zwischen <see cref="IImporter.Parse"/> und <see cref="IImporter.Normalize"/> desselben Laufs.</summary>
    public Dictionary<string, object> Items { get; } = [];

    public void AddWarning(string warning) => _warnings.Add(warning);

    /// <summary>Anzahl gelesener Datenzeilen (ohne Kopfzeile); setzt der Importer in <see cref="IImporter.Parse"/>.</summary>
    public int RowsRead { get; set; }

    public IReadOnlyList<ImportError> Errors => _errors;
    public IReadOnlyCollection<string> UnknownTypes => _unknownTypes;

    public void AddError(int? lineNumber, string reason) => _errors.Add(new ImportError(lineNumber, reason));

    public void AddUnknownType(string typeName) => _unknownTypes.Add(typeName);
}
