namespace Urfair.Core.Tests;

public class ParticipantCsvTests
{
    [Fact]
    public void Reads_first_and_last_name_columns_and_short_parish_names()
    {
        const string csv = "﻿Vorname;Nachname;E-Mail;Geburtsdatum;Pfarrgemeinde\r\n" +
                           "Lena;Huber;lena.huber@example.org;03.05.2012;St. Markus\r\n" +
                           "Paul;Gruber;;;\r\n";
        var r = ParticipantCsv.Parse(csv);
        Assert.Empty(r.Errors);
        Assert.Equal(new ParticipantCsv.Person("Lena Huber", "lena.huber@example.org", new DateOnly(2012, 5, 3), "Pfarrgemeinde Urfahr-St. Markus"), r.People[0]);
        Assert.Equal(new ParticipantCsv.Person("Paul Gruber", null, null, null), r.People[1]);
    }

    [Fact]
    public void Reports_problems_with_row_numbers()
    {
        const string csv = "Name,Email,Date of birth,Parish\n,a@b.at,,\nLena,keine-mail,,\nPaul,,31.02.2012,\nAnna,,,Linz-Süd\nMax,,,\nmax,,,\n";
        var codes = ParticipantCsv.Parse(csv).Errors.Select(e => (e.Code, (int)e.Args!["row"])).ToList();
        Assert.Equal([("csvNameMissing", 2), ("csvEmailInvalid", 3), ("csvDateInvalid", 4), ("csvParishUnknown", 5), ("csvDuplicate", 7)], codes);
        Assert.Equal("csvColumnsMissing", ParticipantCsv.Parse("Vorname;Ort\nA;B").Errors.Single().Code);
    }

    [Fact]
    public void Written_list_can_be_read_again()
    {
        var people = new[] { new ParticipantCsv.Person("Lena Huber", "lena@example.org", new DateOnly(2012, 5, 3), Parishes.Junia) };
        Assert.Equal(people, ParticipantCsv.Parse(ParticipantCsv.Write(people)).People);
    }
}
