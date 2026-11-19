namespace Urfair.Core;

/// <summary>Activities of the confirmation preparation in Linz-Urfahr 2026/27 plus invented sample participants for the demo.</summary>
public static class SampleData
{
    private const string YouthChurch = "Grüner Anker, Jugendkirche Linz";

    public static IReadOnlyList<Activity> Activities()
    {
        var list = new List<Activity>();
        void A(string id, string title, int popularity, string? restrictedTo, params (string start, string? end, string location, int? capacity, bool flexible)[] sessions) =>
            list.Add(new Activity(id, title,
                sessions.Select((s, i) => new Session($"{id}#{i}", id, DateTime.Parse(s.start), s.end is null ? null : DateTime.Parse(s.end), s.location, s.capacity, s.flexible)).ToList(),
                restrictedTo, popularity));

        A("advent", "Bastle deinen eigenen Adventskranz", 8, null,
            ("2026-11-21T14:00", "2026-11-21T16:00", "Pfarrsaal Hl. Geist", 7, false),
            ("2026-11-28T15:00", "2026-11-28T17:00", "Pfarrsaal St. Markus", 7, false));
        A("krafthafen", "KRAFT HAFEN", 2, null,
            ("2026-11-28T18:00", "2026-11-28T19:30", YouthChurch, null, false),
            ("2027-01-30T18:00", "2027-01-30T19:30", YouthChurch, null, false),
            ("2027-02-27T18:00", "2027-02-27T19:30", YouthChurch, null, false),
            ("2027-04-24T18:00", "2027-04-24T19:30", YouthChurch, null, false));
        A("krippe", "Kripperlbauen in Lichtenberg", 5, null, ("2026-12-15T18:00", "2026-12-15T20:00", "Lichtenberg", 10, false));
        A("silvester", "Tschüss 2026 und Hallo 2027!", 3, null, ("2026-12-28T17:00", "2026-12-28T19:30", "St. Markus", 20, false));
        A("nacht", "NACHT QUARTIER", 9, null, ("2027-02-12T17:00", "2027-02-13T09:00", YouthChurch, 25, false));
        A("kitchen", "Heavens Kitchen – Himmlische Küche", 8, null,
            ("2027-02-19T14:00", "2027-02-19T22:00", "St. Magdalena", 10, false),
            ("2027-04-09T14:00", "2027-04-09T22:00", "Lichtenberg", 10, false));
        A("peace", "Peace – mit Gott, anderen und mir selbst", 2, null, ("2027-02-26T18:00", "2027-02-26T20:00", "Christkönig", 25, false));
        A("gehoerlos", "Ein-Blick in die Welt der Gehörlosen", 3, null, ("2027-02-28T09:20", "2027-02-28T11:00", "Urfahr-St. Josef", null, false));
        A("freigeist", "FREI GEIST", 6, null,
            ("2027-03-03T17:00", "2027-03-03T18:00", YouthChurch, 25, false),
            ("2027-03-17T17:00", "2027-03-17T18:00", YouthChurch, 25, false));
        A("spirinight", "Spirinight", 3, null, ("2027-04-10T15:15", "2027-04-10T21:15", "Mariendom Linz", 20, false));
        A("burschen", "„Wann ist ein Mann ein Mann?!“ Burschenwochenende", 5, "m", ("2027-04-16T16:00", "2027-04-17T16:00", "Lichtenberg", 15, false));
        A("radl", "Radltour zu Heiligen Orten", 3, null, ("2027-04-20T16:00", "2027-04-20T20:00", "Treffpunkt St. Markus", 25, false));
        A("brot", "Brot und Rosen – Rundgang durch das obdachlose Linz", 5, null, ("2027-04-23T15:00", "2027-04-23T17:00", "Kirche Hl. Familie", 12, false));
        A("maedels", "Mädelswochenende", 5, "w", ("2027-04-30T17:00", "2027-05-02T13:00", "Ort folgt", 25, false));
        A("hochseil", "„Ich will hoch hinaus!“ Hochseilgarten mit Pate/Patin", 9, null,
            ("2027-05-01T09:00", "2027-05-01T12:30", "Kirchschlag", 12, false),
            ("2027-05-01T14:00", "2027-05-01T17:30", "Kirchschlag", 12, false),
            ("2027-05-08T09:00", "2027-05-08T12:30", "Kirchschlag", 12, false),
            ("2027-05-08T14:00", "2027-05-08T17:30", "Kirchschlag", 12, false));
        A("misterx", "MISTER X", 6, null, ("2027-05-04T10:00", "2027-05-04T12:00", "Ort folgt", 30, false));
        A("kino", "GANZ GROSSES KINO!", 10, null, ("2027-05-10T17:00", "2027-05-10T21:00", "Cineplexx, Movimiento oder City-Kino", 60, false));
        A("geisterfahrer", "GeistErfahrer", 3, null, ("2027-05-11T18:00", "2027-05-11T20:00", "Christkönig", 20, false));
        A("magdaman", "MAGDAMAN – Hackln für den guten Zweck", 3, null, ("2027-05-15T00:00", null, "Pfarrheim St. Magdalena", null, true));
        A("espoto", "Espoto", 2, null, ("2027-06-30T00:00", null, "Pfarrkirche St. Magdalena", null, true));
        A("altenheim", "Besuch im Altenheim", 4, null, ("2027-06-30T00:00", null, "SZ Franz Hillinger", 15, true));
        return list;
    }

    private static readonly string[] FemaleNames = "Anna Lena Sophie Marie Hannah Lea Laura Sarah Julia Emma Mia Valentina Katharina Magdalena Theresa Johanna Clara Elena Nina Paula Selina Viktoria".Split(' ');
    private static readonly string[] MaleNames = "Lukas David Jakob Tobias Felix Maximilian Elias Paul Jonas Simon Florian Moritz Leon Fabian Matthias Noah Valentin Raphael Samuel Benedikt Luca Niklas".Split(' ');

    /// <summary>Generates valid random ballots. Popular activities are chosen more often and with more points.</summary>
    public static List<Ballot> Participants(int count, Rules r, IReadOnlyList<Activity> activities, int seed)
    {
        var rnd = new Random(seed);
        var units = r.PointBudget / r.Step;
        var maxUnits = Math.Max(1, r.MaxPerActivity / r.Step);
        var result = new List<Ballot>();
        for (var i = 0; i < count; i++)
        {
            var gender = rnd.NextDouble() < 0.5 ? "w" : "m";
            var names = gender == "w" ? FemaleNames : MaleNames;
            var name = $"{names[rnd.Next(names.Length)]} {"ABDEFGHKLMNPRSTWZ"[rnd.Next(17)]}.";
            var pool = activities.Where(a => a.RestrictedTo is null || a.RestrictedTo == gender).ToList();
            var kMin = Math.Max(r.MinActivities, (int)Math.Ceiling(units / (double)maxUnits));
            var k = Math.Min(pool.Count, kMin + rnd.Next(3));
            // Weighted sampling without replacement (Efraimidis–Spirakis)
            var chosen = pool.Select(a => (a, key: Math.Pow(rnd.NextDouble(), 1.0 / a.Popularity)))
                .OrderByDescending(x => x.key).Take(k).Select(x => x.a).ToList();

            var u = chosen.Select(_ => 1).ToArray();
            var rest = units - k;
            while (rest > 0)
            {
                var weights = u.Select((x, j) => x < maxUnits ? 1 / Math.Pow(j + 1, 1.1) : 0).ToArray();
                var total = weights.Sum();
                if (total == 0) break;
                var pick = rnd.NextDouble() * total;
                var j = 0;
                while (pick > weights[j]) { pick -= weights[j]; j++; }
                u[j]++;
                rest--;
            }

            var points = chosen.Select((a, j) => (a.Id, P: u[j] * r.Step)).ToDictionary(x => x.Id, x => x.P);
            var unavailable = new HashSet<string>();
            foreach (var a in chosen.Where(a => a.Sessions.Count > 1))
            {
                foreach (var s in a.Sessions) if (rnd.NextDouble() < 0.22) unavailable.Add(s.Id);
                if (a.Sessions.All(s => unavailable.Contains(s.Id))) unavailable.Remove(a.Sessions[rnd.Next(a.Sessions.Count)].Id);
            }
            result.Add(new Ballot($"sample-{i}", name, points, unavailable, gender));
        }
        return result;
    }
}
