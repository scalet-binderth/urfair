using System.Globalization;
using System.Text;

namespace Urfair.Core;

/// <summary>
/// Reads activities from a CSV table as Excel saves it ("CSV UTF-8" or plain "CSV", separator ; or ,).
/// One row per date; rows with the same title form one activity with several dates.
/// Column names in German or English, in any order:
/// Titel, Beschreibung, Gruppe, Pfarre, Leitung, Leitung E-Mail, Teilnahme (m/w), Beginn, Ende, Ort, Plätze (empty = unlimited),
/// Nach Vereinbarung (ja).
/// </summary>
public static class ActivityCsv
{
    public sealed record Result(IReadOnlyList<Activity> Activities, IReadOnlyList<ValidationError> Errors);

    private static readonly Dictionary<string, string> Columns = new()
    {
        ["titel"] = "title", ["title"] = "title",
        ["beschreibung"] = "description", ["description"] = "description",
        ["gruppe"] = "group", ["group"] = "group",
        ["pfarre"] = "parish", ["parish"] = "parish",
        ["leitung"] = "leader", ["leader"] = "leader", ["ledby"] = "leader",
        ["leitungemail"] = "leaderemail", ["leitungmail"] = "leaderemail", ["leaderemail"] = "leaderemail", ["ledbyemail"] = "leaderemail",
        ["teilnahme"] = "restriction", ["restriction"] = "restriction", ["whocantakepart"] = "restriction",
        ["beginn"] = "start", ["start"] = "start",
        ["ende"] = "end", ["end"] = "end",
        ["ort"] = "location", ["location"] = "location",
        ["plätze"] = "seats", ["plaetze"] = "seats", ["seats"] = "seats",
        ["nachvereinbarung"] = "flexible", ["byarrangement"] = "flexible", ["datebyarrangement"] = "flexible",
    };

    private static readonly string[] DateFormats =
    [
        "d.M.yyyy H:mm", "d.M.yyyy HH:mm", "d.M.yy H:mm", "d.M.yyyy", "d.M.yy",
        "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd",
        "d/M/yyyy H:mm", "d/M/yyyy",
    ];

    public static Result Parse(string text, IReadOnlyList<ActivityGroup> groups)
    {
        var errors = new List<ValidationError>();
        var rows = Csv.ReadRows(text);
        if (rows.Count == 0) return new([], [E("csvEmpty", 0)]);

        var Cell = Csv.Header(rows[0], Columns, out var header);
        var missing = new[] { "title", "start" }.Where(c => !header.Contains(c)).ToList();
        if (missing.Count > 0) return new([], [E("csvColumnsMissing", 1, ("columns", string.Join(", ", missing)))]);

        // Activities in order of first appearance; the first row of a title carries its description, group and restriction.
        var drafts = new List<(int Row, string Title, string? Description, string? GroupId, string? RestrictedTo, string? Parish, string? Leader, string? LeaderEmail, List<Session> Sessions)>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var line = r + 1;
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            var title = Cell(row, "title");
            if (title.Length == 0) { errors.Add(E("csvTitleMissing", line)); continue; }

            var draft = drafts.FirstOrDefault(d => d.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (draft.Title is null)
            {
                var groupName = Cell(row, "group");
                var group = groupName.Length == 0 ? null : groups.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
                if (groupName.Length > 0 && group is null) errors.Add(E("csvGroupUnknown", line, ("value", groupName)));
                var restriction = Restriction(Cell(row, "restriction"));
                if (restriction == "?") { errors.Add(E("csvRestrictionInvalid", line, ("value", Cell(row, "restriction")))); restriction = null; }
                var parishText = Cell(row, "parish");
                var parish = Parishes.Match(parishText);
                if (parishText.Length > 0 && parish is null) errors.Add(E("csvParishUnknown", line, ("value", parishText)));
                draft = (line, title, OrNull(Cell(row, "description")), group?.Id, restriction, parish, OrNull(Cell(row, "leader")), OrNull(Cell(row, "leaderemail")), []);
                drafts.Add(draft);
            }

            var flexible = IsYes(Cell(row, "flexible"));
            var startText = Cell(row, "start");
            if (!TryDate(startText, null, out var start)) { errors.Add(E("csvDateInvalid", line, ("value", startText))); continue; }
            DateTime? end = null;
            var endText = Cell(row, "end");
            if (!flexible && endText.Length > 0)
            {
                if (TryDate(endText, start, out var e)) end = e;
                else { errors.Add(E("csvDateInvalid", line, ("value", endText))); continue; }
            }
            int? seats = null;
            var seatsText = Cell(row, "seats");
            if (seatsText.Length > 0)
            {
                if (int.TryParse(seatsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) seats = n;
                else { errors.Add(E("csvSeatsInvalid", line, ("value", seatsText))); continue; }
            }
            draft.Sessions.Add(new Session($"row{line}", "", start, end, Cell(row, "location"), seats, flexible));
        }

        var activities = new List<Activity>();
        foreach (var (d, i) in drafts.Select((d, i) => (d, i)))
        {
            var id = $"import{i}";
            var activity = new Activity(id, d.Title, d.Sessions.Select(s => s with { ActivityId = id }).ToList(), d.RestrictedTo, GroupId: d.GroupId,
                Description: d.Description, Parish: d.Parish, Leader: d.Leader, LeaderEmail: d.LeaderEmail);
            // Activity rules (end after start, seats ≥ 1 …), reported with the activity's first row.
            errors.AddRange(Validation.Check(activity).Select(e => e with { Args = WithRow(e.Args, d.Row) }));
            activities.Add(activity);
        }
        return new(activities, errors);
    }

    /// <summary>Writes activities in the same format (one row per date, separator ;) so a list can be edited in Excel and imported again.</summary>
    public static string Write(IReadOnlyList<Activity> activities, IReadOnlyList<ActivityGroup> groups)
    {
        var sb = new StringBuilder(Csv.Line(["Titel", "Beschreibung", "Gruppe", "Pfarre", "Leitung", "Leitung E-Mail", "Teilnahme", "Beginn", "Ende", "Ort", "Plätze", "Nach Vereinbarung"]));
        foreach (var a in activities)
        foreach (var (s, i) in a.Sessions.Select((s, i) => (s, i)))
        {
            var end = s.End is not { } e ? "" : e.Date == s.Start.Date ? e.ToString("HH:mm") : e.ToString("dd.MM.yyyy HH:mm");
            string[] cells =
            [
                // Description, group and participation only on the first row of an activity.
                a.Title, i > 0 ? "" : a.Description ?? "", i > 0 ? "" : groups.FirstOrDefault(g => g.Id == a.GroupId)?.Name ?? "",
                i > 0 ? "" : a.Parish ?? "", i > 0 ? "" : a.Leader ?? "", i > 0 ? "" : a.LeaderEmail ?? "", i > 0 ? "" : a.RestrictedTo ?? "",
                s.Flexible ? s.Start.ToString("dd.MM.yyyy") : s.Start.ToString("dd.MM.yyyy HH:mm"), end, s.Location,
                s.Capacity?.ToString(CultureInfo.InvariantCulture) ?? "", s.Flexible ? "ja" : "",
            ];
            sb.Append(Csv.Line(cells));
        }
        return sb.ToString();
    }

    private static ValidationError E(string code, int row, params (string Key, object Value)[] args) => Csv.Error(code, row, args);

    private static IReadOnlyDictionary<string, object> WithRow(IReadOnlyDictionary<string, object>? args, int row)
    {
        var copy = args?.ToDictionary() ?? [];
        copy["row"] = row;
        return copy;
    }

    private static string? OrNull(string value) => value.Length == 0 ? null : value;

    private static bool IsYes(string value) =>
        value.ToLowerInvariant() is "ja" or "j" or "x" or "yes" or "y" or "1" or "true" or "wahr";

    /// <summary>"" → everyone, "m"/"w" (or words for boys/girls) → restricted, anything else → "?".</summary>
    private static string? Restriction(string value) => value.ToLowerInvariant() switch
    {
        "" or "alle" or "all" or "everyone" => null,
        "m" or "burschen" or "nur burschen" or "boys" or "boys only" => "m",
        "w" or "mädchen" or "nur mädchen" or "girls" or "girls only" => "w",
        _ => "?",
    };

    /// <summary>Date with time, a date alone, or (for the end) just a time on the start's day.</summary>
    private static bool TryDate(string text, DateTime? day, out DateTime value)
    {
        if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return true;
        if (day is { } d && TimeSpan.TryParseExact(text, ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var time))
        {
            value = d.Date + time;
            return true;
        }
        return false;
    }
}
