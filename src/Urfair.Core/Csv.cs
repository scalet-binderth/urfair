using System.Text;

namespace Urfair.Core;

/// <summary>Shared CSV handling of <see cref="ActivityCsv"/> and <see cref="ParticipantCsv"/>: Excel's format, separator ; (or , or tab).</summary>
internal static class Csv
{
    /// <summary>
    /// Reads the header row with the known column names (German or English, any order, letter case and hints such as
    /// "Teilnahme (m/w)" ignored) and returns the cell reader: trimmed text of a column, "" if missing.
    /// </summary>
    public static Func<List<string>, string, string> Header(List<string> header, IReadOnlyDictionary<string, string> columns, out List<string?> found)
    {
        var names = header.Select(h => columns.GetValueOrDefault(Normalise(h))).ToList();
        found = names;
        return (row, column) =>
        {
            var i = names.IndexOf(column);
            var cell = i >= 0 && i < row.Count ? row[i].Trim() : "";
            // Written by Quote to keep Excel from reading the cell as a formula.
            return cell.Length > 1 && cell[0] == '\'' && IsFormulaStart(cell[1]) ? cell[1..] : cell;
        };
    }

    /// <summary>Only letters, lower case, without a hint in brackets: "Plätze (leer = unbegrenzt)" → "plätze".</summary>
    private static string Normalise(string header)
    {
        var bracket = header.IndexOf('(');
        return new((bracket >= 0 ? header[..bracket] : header).ToLowerInvariant().Where(char.IsLetter).ToArray());
    }

    /// <summary>One row of cells, separator ;.</summary>
    public static string Line(IEnumerable<string> cells) => string.Join(';', cells.Select(Quote)) + "\r\n";

    /// <summary>Quotes where needed; a leading = + - @ gets an apostrophe so Excel shows the text instead of running a formula.</summary>
    private static string Quote(string value)
    {
        if (value.Length > 0 && IsFormulaStart(value[0])) value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static bool IsFormulaStart(char c) => c is '=' or '+' or '-' or '@';

    /// <summary>An error of a row (1 = header row).</summary>
    public static ValidationError Error(string code, int row, params (string Key, object Value)[] args) =>
        new(code, args.Append((Key: "row", Value: (object)row)).ToDictionary(a => a.Key, a => a.Value));

    /// <summary>Splits CSV text into rows and cells; handles a byte order mark, quotes, doubled quotes and line breaks inside quotes.</summary>
    public static List<List<string>> ReadRows(string text)
    {
        text = text.TrimStart('﻿');
        var firstLine = text.Split('\n')[0];
        var separator = new[] { ';', ',', '\t' }.MaxBy(c => firstLine.Count(x => x == c));
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == separator) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                rows.Add(row); row = [];
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
