using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace Urfair.Core;

/// <summary>
/// Reads a participant list as Excel saves it (separator ; or ,). Columns in any order, German or English:
/// Name (or Vorname + Nachname), E-Mail, Geburtsdatum, Taufdatum, Pfarrgemeinde (official name or short form). Only the name is required.
/// </summary>
public static class ParticipantCsv
{
    public sealed record Person(string Name, string? Email, DateOnly? BirthDate, string? Parish, DateOnly? BaptismDate = null);
    public sealed record Result(IReadOnlyList<Person> People, IReadOnlyList<ValidationError> Errors);

    private static readonly Dictionary<string, string> Columns = new()
    {
        ["name"] = "name",
        ["vorname"] = "first", ["firstname"] = "first",
        ["nachname"] = "last", ["familienname"] = "last", ["lastname"] = "last", ["surname"] = "last",
        ["email"] = "email", ["mail"] = "email", ["emailadresse"] = "email",
        ["geburtsdatum"] = "birth", ["geboren"] = "birth", ["dateofbirth"] = "birth", ["birthdate"] = "birth",
        ["taufdatum"] = "baptism", ["getauft"] = "baptism", ["dateofbaptism"] = "baptism", ["baptismdate"] = "baptism", ["baptism"] = "baptism",
        ["pfarrgemeinde"] = "parish", ["pfarre"] = "parish", ["parish"] = "parish",
    };

    private static readonly string[] DateFormats = ["d.M.yyyy", "d.M.yy", "yyyy-MM-dd", "d/M/yyyy"];

    public static Result Parse(string text)
    {
        var errors = new List<ValidationError>();
        var rows = ActivityCsv.ReadRows(text.TrimStart('﻿'));
        if (rows.Count == 0) return new([], [E("csvEmpty", 0)]);

        var header = rows[0].Select(ActivityCsv.Normalise).Select(h => Columns.GetValueOrDefault(h)).ToList();
        if (!header.Contains("name") && !(header.Contains("first") && header.Contains("last")))
            return new([], [E("csvColumnsMissing", 1, ("columns", "Name / Vorname, Nachname"))]);

        string Cell(List<string> row, string column)
        {
            var i = header.IndexOf(column);
            return i >= 0 && i < row.Count ? row[i].Trim() : "";
        }

        var people = new List<Person>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var line = r + 1;
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            var name = Cell(row, "name");
            if (name.Length == 0) name = $"{Cell(row, "first")} {Cell(row, "last")}".Trim();
            if (name.Length == 0) { errors.Add(E("csvNameMissing", line)); continue; }
            if (name.Length > 120) { errors.Add(E("textTooLong", line, ("max", 120))); continue; }
            if (people.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { errors.Add(E("csvDuplicate", line, ("value", name))); continue; }

            var email = Cell(row, "email");
            if (email.Length > 0 && !IsEmail(email)) { errors.Add(E("csvEmailInvalid", line, ("value", email))); continue; }

            if (!TryDate(Cell(row, "birth"), out var birth) || !TryDate(Cell(row, "baptism"), out var baptism))
            {
                var bad = TryDate(Cell(row, "birth"), out _) ? Cell(row, "baptism") : Cell(row, "birth");
                errors.Add(E("csvDateInvalid", line, ("value", bad)));
                continue;
            }

            var parishText = Cell(row, "parish");
            var parish = Parishes.Match(parishText);
            if (parishText.Length > 0 && parish is null) { errors.Add(E("csvParishUnknown", line, ("value", parishText))); continue; }

            people.Add(new Person(name, email.Length == 0 ? null : email, birth, parish, baptism));
        }
        return new(people, errors);
    }

    /// <summary>An empty cell is a valid "no date".</summary>
    private static bool TryDate(string text, out DateOnly? date)
    {
        date = null;
        if (text.Length == 0) return true;
        if (!DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
        date = d;
        return true;
    }

    /// <summary>Same format back (separator ;), e.g. as a template or to edit the list in Excel.</summary>
    public static string Write(IEnumerable<Person> people)
    {
        var sb = new StringBuilder("Name;E-Mail;Geburtsdatum;Taufdatum;Pfarrgemeinde\r\n");
        foreach (var p in people)
            sb.Append(string.Join(';', new[] { p.Name, p.Email ?? "", p.BirthDate?.ToString("dd.MM.yyyy") ?? "", p.BaptismDate?.ToString("dd.MM.yyyy") ?? "", p.Parish ?? "" }.Select(ActivityCsv.Quote))).Append("\r\n");
        return sb.ToString();
    }

    public static bool IsEmail(string text) =>
        text.Length <= 200 && MailAddress.TryCreate(text, out var a) && a.Address == text && a.Host.Contains('.');

    private static ValidationError E(string code, int row, params (string Key, object Value)[] args) =>
        new(code, args.Append(("row", row)).ToDictionary(a => a.Key, a => a.Value));
}
