namespace KryptoSteuer.Importers.Tests;

internal static class Fixtures
{
    public static byte[] Read(string relativePath) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", relativePath));
}
