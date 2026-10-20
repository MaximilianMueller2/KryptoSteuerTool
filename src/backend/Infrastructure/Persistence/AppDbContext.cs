using Microsoft.EntityFrameworkCore;

namespace KryptoSteuer.Infrastructure.Persistence;

/// <summary>
/// EF-Core-Kontext. In P1 noch ohne Entitäten; das Datenmodell (Ledger) folgt in P2.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
