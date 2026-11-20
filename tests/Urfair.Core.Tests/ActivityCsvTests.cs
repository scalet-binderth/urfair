namespace Urfair.Core.Tests;

public class ActivityCsvTests
{
    [Fact]
    public void Reads_excel_csv_with_several_dates_per_activity()
    {
        const string csv = "﻿Titel;Beschreibung;Gruppe;Teilnahme;Beginn;Ende;Ort;Plätze;Nach Vereinbarung\r\n" +
                           "Adventskranz;\"Binden; mit Zweigen\";Grüne Projekte;;21.11.2026 14:00;16:00;Pfarrsaal;7;\r\n" +
                           "Adventskranz;;;;28.11.2026 15:00;17:00;St. Markus;7;\r\n" +
                           "Altenheim;;blaue projekte;;30.06.2027;;SZ Hillinger;;ja\r\n" +
                           "Burschenwochenende;;Grüne Projekte;m;16.04.2027 16:00;17.04.2027 16:00;Lichtenberg;15;\r\n";
        var result = ActivityCsv.Parse(csv, SampleData.Groups);

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Activities.Count);
        var advent = result.Activities[0];
        Assert.Equal("Binden; mit Zweigen", advent.Description);
        Assert.Equal("green", advent.GroupId);
        Assert.Equal(2, advent.Sessions.Count);
        Assert.Equal(new DateTime(2026, 11, 21, 16, 0, 0), advent.Sessions[0].End);
        Assert.Equal(14, advent.Seats);
        Assert.True(result.Activities[1].Sessions[0].Flexible);
        Assert.Null(result.Activities[1].Seats);
        Assert.Equal("blue", result.Activities[1].GroupId);
        Assert.Equal("m", result.Activities[2].RestrictedTo);
    }

    [Fact]
    public void Reports_problems_with_row_numbers()
    {
        const string csv = "Title,Start,End,Seats,Group\nKino,soon,,10,\n,10.5.2027 17:00,,,\nX,10.5.2027 17:00,16:00,5,Rot\n";
        var errors = ActivityCsv.Parse(csv, SampleData.Groups).Errors;
        Assert.Contains(errors, e => e.Code == "csvDateInvalid" && (int)e.Args!["row"] == 2);
        Assert.Contains(errors, e => e.Code == "csvTitleMissing" && (int)e.Args!["row"] == 3);
        Assert.Contains(errors, e => e.Code == "csvGroupUnknown" && (int)e.Args!["row"] == 4);
        Assert.Contains(errors, e => e.Code == "endBeforeStart" && (int)e.Args!["row"] == 4);
        Assert.Equal("csvColumnsMissing", ActivityCsv.Parse("Name;Ort\nA;B", []).Errors.Single().Code);
    }

    [Fact]
    public void Written_list_can_be_read_again()
    {
        var activities = SampleData.Activities();
        var again = ActivityCsv.Parse(ActivityCsv.Write(activities, SampleData.Groups), SampleData.Groups);
        Assert.Empty(again.Errors);
        Assert.Equal(activities.Select(a => (a.Title, a.GroupId, a.RestrictedTo, a.Description, a.Parish, a.Leader, a.Sessions.Count, a.Seats)),
            again.Activities.Select(a => (a.Title, a.GroupId, a.RestrictedTo, a.Description, a.Parish, a.Leader, a.Sessions.Count, a.Seats)));
        Assert.Equal(activities.SelectMany(a => a.Sessions.Select(s => (s.Start, s.End, s.Location, s.Flexible))),
            again.Activities.SelectMany(a => a.Sessions.Select(s => (s.Start, s.End, s.Location, s.Flexible))));
    }

    [Fact]
    public void Reads_optional_parish_and_leader_columns()
    {
        const string csv = "Titel;Pfarre;Leitung;Beginn\nKuchenbuffet;;;13.06.2027 13:00\nKinderschminken;St. Markus;Eva;13.06.2027 13:00\nTombola;Urfahr-Süd;;13.06.2027 13:00\n";
        var result = ActivityCsv.Parse(csv, []);
        var a = result.Activities;
        Assert.Contains(result.Errors, e => e.Code == "csvParishUnknown" && (int)e.Args!["row"] == 4);
        Assert.Null(a[0].Parish);
        Assert.Null(a[0].Leader);
        Assert.Equal(("Pfarrgemeinde Urfahr-St. Markus", "Eva"), (a[1].Parish, a[1].Leader));
    }

    [Theory]
    [InlineData("St. Markus", "Pfarrgemeinde Urfahr-St. Markus")]
    [InlineData("urfahr-hl. geist", "Pfarrgemeinde Urfahr-Hl. Geist")]
    [InlineData("Pfarrgemeinde Urfahr-Lichtenberg", "Pfarrgemeinde Urfahr-Lichtenberg")]
    [InlineData("Pöstlingberg", "Urfahr-Pöstlingberg")]
    [InlineData("Junia", Parishes.Junia)]
    [InlineData("St. Junia", Parishes.Junia)]
    [InlineData("Linz-Süd", null)]
    [InlineData("", null)]
    public void Parish_short_forms_are_matched(string text, string? expected) => Assert.Equal(expected, Parishes.Match(text));
}
