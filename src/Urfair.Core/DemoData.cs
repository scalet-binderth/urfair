namespace Urfair.Core;

/// <summary>Additional invented FairSlots and demo people for presentations.</summary>
public static class DemoData
{
    public sealed record Person(string Key, string Name);

    /// <summary>A: no FairSlot, B: two open, C: two open + one published, D: two open + one closed + one published.</summary>
    public static readonly Person A = new("A", "Anna Ammer");
    public static readonly Person B = new("B", "Ben Berger");
    public static readonly Person C = new("C", "Clara Czerny");
    public static readonly Person D = new("D", "David Doppler");
    public static readonly IReadOnlyList<Person> People = [A, B, C, D];

    public static readonly Rules ExcursionRules = new(PointBudget: 100, MinActivities: 2, MaxPerActivity: 70, PerParticipant: 1);
    public static readonly Rules HolidayRules = new(PointBudget: 100, MinActivities: 3, MaxPerActivity: 50, PerParticipant: 2);
    public static readonly Rules CampRules = new(PointBudget: 100, MinActivities: 2, MaxPerActivity: 60, PerParticipant: 1);
    public static readonly Rules FestivalRules = new(PointBudget: 100, MinActivities: 3, MaxPerActivity: 50, PerParticipant: 2);

    /// <summary>Open vote without any ballots, to try voting live in a presentation.</summary>
    public static IReadOnlyList<Activity> ParishFestival() => Build("fest",
        // Shifts one after another, so that two activities per person can fit.
        ("Aufbau am Vortag", 4, ("2027-06-12T15:00", "2027-06-12T19:00", "Pfarrgarten St. Markus", 10)),
        ("Grillstand", 8, ("2027-06-13T10:00", "2027-06-13T13:00", "Pfarrgarten St. Markus", 6)),
        ("Kinderschminken", 7, ("2027-06-13T13:00", "2027-06-13T15:00", "Pfarrsaal St. Markus", 4)),
        ("Kuchenbuffet", 6, ("2027-06-13T15:00", "2027-06-13T17:00", "Pfarrsaal St. Markus", 5)),
        ("Musik und Ton am Abend", 5, ("2027-06-13T17:00", "2027-06-13T21:00", "Bühne Pfarrgarten", 3)));

    public static IReadOnlyList<Activity> Excursion() => Build("mini",
        ("Tiergarten Walding", 9, ("2026-10-03T09:00", "2026-10-03T16:00", "Tiergarten Walding", 25)),
        ("Pöstlingbergbahn und Grottenbahn", 7, ("2026-10-03T09:00", "2026-10-03T13:00", "Hauptplatz Linz", 20)),
        ("Kletterwald Linz", 8, ("2026-10-03T10:00", "2026-10-03T15:00", "Kletterwald Pöstlingberg", 15)),
        ("Ars Electronica Center", 5, ("2026-10-03T10:00", "2026-10-03T14:00", "Ars Electronica Center", 30)));

    public static IReadOnlyList<Activity> HolidayProgramme() => Build("ferien",
        ("Kochen mit Oma", 7, ("2026-07-14T09:00", "2026-07-14T13:00", "Pfarrheim St. Markus", 8)),
        ("Parkbad-Nachmittag", 8, ("2026-07-21T13:00", "2026-07-21T18:00", "Parkbad Linz", 25)),
        ("Waldtag mit dem Förster", 6, ("2026-07-28T09:00", "2026-07-28T15:00", "Pöstlingberg", 15)),
        ("Kreativwerkstatt", 5, ("2026-08-04T09:00", "2026-08-04T12:00", "Pfarrsaal Hl. Geist", 12)),
        ("Fußballturnier", 9, ("2026-08-11T14:00", "2026-08-11T18:00", "Sportplatz Urfahr", 30)));

    public static IReadOnlyList<Activity> Camp() => Build("lager",
        ("Sommerlager Woche 1 (8–10 Jahre)", 8, ("2027-07-11T10:00", "2027-07-17T12:00", "Jungscharhaus Kirchschlag", 40)),
        ("Sommerlager Woche 2 (11–13 Jahre)", 8, ("2027-07-18T10:00", "2027-07-24T12:00", "Jungscharhaus Kirchschlag", 40)),
        ("Schnuppertage (6–7 Jahre)", 5, ("2027-07-26T09:00", "2027-07-28T16:00", "Pfarrheim St. Magdalena", 20)));

    private static List<Activity> Build(string prefix, params (string Title, int Popularity, (string Start, string End, string Location, int Capacity) Session)[] items) =>
        items.Select((x, i) =>
        {
            var id = $"{prefix}{i}";
            return new Activity(id, x.Title,
                [new Session($"{id}#0", id, DateTime.Parse(x.Session.Start), DateTime.Parse(x.Session.End), x.Session.Location, x.Session.Capacity)],
                null, x.Popularity);
        }).ToList();
}
