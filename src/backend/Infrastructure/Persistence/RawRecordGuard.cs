using KryptoSteuer.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace KryptoSteuer.Infrastructure.Persistence;

/// <summary>Rohdaten werden nie geändert oder gelöscht; nur Einfügen ist erlaubt.</summary>
internal static class RawRecordGuard
{
    public static void EnsureUntouched(ChangeTracker changeTracker)
    {
        var violated = changeTracker.Entries<RawRecord>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (violated)
            throw new InvalidOperationException(
                "RawRecord ist unveränderlich: Korrekturen sind eigene Overrides mit Audit-Log.");
    }
}
