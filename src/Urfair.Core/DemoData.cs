namespace Urfair.Core;

/// <summary>Additional invented FairSlots and demo people for presentations.</summary>
public static class DemoData
{
    public sealed record Person(string Key, string Name, string Email, DateOnly BirthDate, string Parish, DateOnly? BaptismDate = null);

    /// <summary>A: no FairSlot, B: two in registration, C: two in registration + one published, D: additionally one closed.</summary>
    public static readonly Person A = new("A", "Anna Ammer", "anna.ammer@example.org", new(2012, 3, 14), "Pfarrgemeinde Urfahr-St. Josef");
    public static readonly Person B = new("B", "Ben Berger", "ben.berger@example.org", new(2012, 7, 2), "Pfarrgemeinde Urfahr-St. Markus", new(2012, 9, 16));
    public static readonly Person C = new("C", "Clara Czerny", "clara.czerny@example.org", new(2011, 11, 23), "Pfarrgemeinde Urfahr-Hl. Geist", new(2012, 4, 8));
    public static readonly Person D = new("D", "David Doppler", "david.doppler@example.org", new(2012, 1, 9), "Pfarrgemeinde Urfahr-Christkönig");
    public static readonly IReadOnlyList<Person> People = [A, B, C, D];

    /// <summary>Demo person A stays without any vote, even for votes open to all (shows the "nothing assigned" case).</summary>
    public static readonly string NobodyName = A.Name.ToLowerInvariant();

    public static readonly Rules ExcursionRules = new(PointBudget: 100, MinActivities: 2, MaxPerActivity: 70, PerParticipant: 1);
    public static readonly Rules HolidayRules = new(PointBudget: 100, MinActivities: 3, MaxPerActivity: 50, PerParticipant: 2);
    public static readonly Rules CampRules = new(PointBudget: 100, MinActivities: 2, MaxPerActivity: 60, PerParticipant: 1);
    public static readonly Rules FestivalRules = new(PointBudget: 100, MinActivities: 3, MaxPerActivity: 50, PerParticipant: 2);

    /// <summary>
    /// Open vote without any ballots, to try voting live in a presentation. Shifts on the festival day:
    /// 10–13, 13–15, 15–17, 17–21, so two activities per person can fit. Few seats, so a handful of
    /// testers already compete for the popular ones.
    /// </summary>
    public static IReadOnlyList<Activity> ParishFestival()
    {
        const string Garden = "Pfarrgarten St. Markus", Hall = "Pfarrsaal St. Markus";
        static (string, string, string, int) S(string from, string to, string where, int seats) => ($"2027-06-13T{from}", $"2027-06-13T{to}", where, seats);
        return BuildMulti("fest",
            ("Aufbau am Vortag", 3, [("2027-06-12T15:00", "2027-06-12T19:00", Garden, 8)]),
            ("Grillstand", 8, [S("10:00", "13:00", Garden, 3), S("15:00", "17:00", Garden, 3)]),
            ("Getränkeausschank", 6, [S("10:00", "13:00", Garden, 2), S("13:00", "15:00", Garden, 2), S("17:00", "21:00", Garden, 3)]),
            ("Kinderschminken", 7, [S("13:00", "15:00", Hall, 2)]),
            ("Kuchenbuffet", 6, [S("13:00", "15:00", Hall, 2), S("15:00", "17:00", Hall, 2)]),
            ("Flohmarkt-Stand", 5, [S("10:00", "13:00", "Kirchenvorplatz", 3)]),
            ("Kinderspiele-Station", 7, [S("10:00", "13:00", Garden, 2), S("15:00", "17:00", Garden, 2)]),
            ("Kasse und Lose", 3, [S("13:00", "15:00", "Eingang Pfarrgarten", 2)]),
            ("Fotografieren", 6, [S("13:00", "17:00", "überall", 1)]),
            ("Musik und Ton am Abend", 8, [S("17:00", "21:00", "Bühne Pfarrgarten", 2)]));
    }

    /// <summary>Two leaders with two activities each, to show the lists per leader. Few seats in the popular ones, to show moving up.</summary>
    public static IReadOnlyList<Activity> Excursion() => Build("mini",
        ("Tiergarten Walding", 9, ("2026-10-03T09:00", "2026-10-03T16:00", "Tiergarten Walding", 10)),
        ("Pöstlingbergbahn und Grottenbahn", 7, ("2026-10-03T09:00", "2026-10-03T13:00", "Hauptplatz Linz", 8)),
        ("Kletterwald Linz", 8, ("2026-10-03T10:00", "2026-10-03T15:00", "Kletterwald Pöstlingberg", 6)),
        ("Ars Electronica Center", 5, ("2026-10-03T10:00", "2026-10-03T14:00", "Ars Electronica Center", 30)))
        .Select((a, i) => i % 2 == 0
            ? a with { Leader = "Miriam Koller", LeaderEmail = "miriam.koller@example.org" }
            : a with { Leader = "Florian Stadler", LeaderEmail = "florian.stadler@example.org" }).ToList();

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

    /// <summary>
    /// Moves all sessions by whole weeks (weekdays stay) so the first one is on <paramref name="firstDay"/> or within the week after,
    /// so demo FairSlots fit their phase whenever the demo is reset.
    /// </summary>
    public static IReadOnlyList<Activity> MovedTo(this IReadOnlyList<Activity> activities, DateOnly firstDay)
    {
        var first = activities.SelectMany(a => a.Sessions).Min(s => s.Start).Date;
        var shift = TimeSpan.FromDays(7 * Math.Ceiling((firstDay.ToDateTime(TimeOnly.MinValue) - first).TotalDays / 7));
        return activities.Select(a => a with { Sessions = a.Sessions.Select(s => s with { Start = s.Start + shift, End = s.End + shift }).ToList() }).ToList();
    }

    private static List<Activity> BuildMulti(string prefix, params (string Title, int Popularity, (string Start, string End, string Location, int Capacity)[] Sessions)[] items) =>
        items.Select((x, i) =>
        {
            var id = $"{prefix}{i}";
            return new Activity(id, x.Title,
                x.Sessions.Select((s, j) => new Session($"{id}#{j}", id, DateTime.Parse(s.Start), DateTime.Parse(s.End), s.Location, s.Capacity)).ToList(),
                null, x.Popularity);
        }).ToList();

    private static List<Activity> Build(string prefix, params (string Title, int Popularity, (string Start, string End, string Location, int Capacity) Session)[] items) =>
        items.Select((x, i) =>
        {
            var id = $"{prefix}{i}";
            return new Activity(id, x.Title,
                [new Session($"{id}#0", id, DateTime.Parse(x.Session.Start), DateTime.Parse(x.Session.End), x.Session.Location, x.Session.Capacity)],
                null, x.Popularity);
        }).ToList();
}
