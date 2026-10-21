using System.Text;

namespace KryptoSteuer.Importers.Common;

/// <summary>Eine CSV-Zeile mit ihrer Zeilennummer in der Datei (1 = Kopfzeile) und dem unveränderten Originaltext.</summary>
public sealed record CsvRow(int LineNumber, string RawText, IReadOnlyList<string> Fields);

/// <summary>
/// Minimaler CSV-Leser (RFC 4180: Anführungszeichen, verdoppelte Anführungszeichen, Zeilenumbrüche in Feldern).
/// Trennzeichen wird aus der Kopfzeile erkannt (Komma oder Semikolon). Entfernt ein UTF-8-BOM.
/// </summary>
public static class CsvReader
{
    public static IEnumerable<CsvRow> Read(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        var delimiter = DetectDelimiter(text);
        var fields = new List<string>();
        var field = new StringBuilder();
        var raw = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var startLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            raw.Append(c);

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        raw.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n') line++;
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                raw.Length -= 1; // Zeilenende gehört nicht zum Rohtext
                fields.Add(field.ToString());
                field.Clear();
                if (!IsBlank(fields))
                    yield return new CsvRow(startLine, raw.ToString(), fields.ToArray());
                fields = [];
                raw.Clear();
                line++;
                startLine = line;
            }
            else
            {
                field.Append(c);
            }
        }

        if (raw.Length > 0 || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (!IsBlank(fields))
                yield return new CsvRow(startLine, raw.ToString(), fields.ToArray());
        }
    }

    private static bool IsBlank(List<string> fields) => fields.All(string.IsNullOrWhiteSpace);

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        return header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
    }
}
