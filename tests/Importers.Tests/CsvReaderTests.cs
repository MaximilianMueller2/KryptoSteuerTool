using System.Text;
using KryptoSteuer.Importers.Common;

namespace KryptoSteuer.Importers.Tests;

public class CsvReaderTests
{
    private static List<CsvRow> Read(string text) => CsvReader.Read(Encoding.UTF8.GetBytes(text)).ToList();

    [Fact]
    public void Liest_Komma_getrennt_mit_Zeilennummern()
    {
        var rows = Read("a,b\n1,2\n3,4\n");

        rows.Should().HaveCount(3);
        rows[1].Fields.Should().Equal("1", "2");
        rows[1].LineNumber.Should().Be(2);
        rows[2].RawText.Should().Be("3,4");
    }

    [Fact]
    public void Erkennt_Semikolon_aus_der_Kopfzeile_und_entfernt_das_BOM()
    {
        var rows = Read("﻿a;b\r\n1;2\r\n");

        rows[0].Fields.Should().Equal("a", "b");
        rows[1].Fields.Should().Equal("1", "2");
        rows[1].RawText.Should().Be("1;2");
    }

    [Fact]
    public void Versteht_Anfuehrungszeichen_Trennzeichen_und_Zeilenumbrueche_in_Feldern()
    {
        var rows = Read("a,b\n\"x,y\",\"sagt \"\"hi\"\"\"\n\"zwei\nZeilen\",z\nletzte,zeile\n");

        rows[1].Fields.Should().Equal("x,y", "sagt \"hi\"");
        rows[2].Fields.Should().Equal("zwei\nZeilen", "z");
        rows[3].LineNumber.Should().Be(5, "der Umbruch im Feld zählt als Zeile");
    }

    [Fact]
    public void Ueberspringt_Leerzeilen_ohne_die_Zeilennummern_zu_verschieben()
    {
        var rows = Read("a,b\n\n1,2\n");

        rows.Should().HaveCount(2);
        rows[1].LineNumber.Should().Be(3);
    }

    [Fact]
    public void Liest_die_letzte_Zeile_auch_ohne_Zeilenende()
    {
        Read("a,b\n1,2").Last().Fields.Should().Equal("1", "2");
    }
}
