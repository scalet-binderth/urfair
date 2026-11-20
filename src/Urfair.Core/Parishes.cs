namespace Urfair.Core;

/// <summary>
/// The Pfarre Urfahr-St. Junia and its Pfarrgemeinden, with the official names from the parish website.
/// Votes and activities pick their parish from this list.
/// </summary>
public static class Parishes
{
    public const string Junia = "Pfarre Urfahr-St. Junia";

    public static readonly IReadOnlyList<string> All =
    [
        Junia,
        "Pfarrgemeinde Urfahr-Christkönig",
        "Pfarrgemeinde Urfahr-Hl. Geist",
        "Pfarrgemeinde Urfahr-Lichtenberg",
        "Pfarrgemeinde Urfahr-St. Josef",
        "Pfarrgemeinde Urfahr-St. Leopold",
        "Pfarrgemeinde Urfahr-St. Magdalena",
        "Pfarrgemeinde Urfahr-St. Markus",
        "Urfahr-Pöstlingberg",
    ];

    /// <summary>
    /// The official name for a full or short form ("St. Markus", "Urfahr-St. Markus", "Junia" …), or null if unknown.
    /// </summary>
    public static string? Match(string? text)
    {
        var key = Key(text ?? "");
        if (key.Length == 0) return null;
        return All.FirstOrDefault(p => Key(p) == key) ?? All.FirstOrDefault(p => Short(Key(p)) == Short(key));
    }

    // Lower case, letters and digits only: "Urfahr-St. Markus" → "urfahrstmarkus".
    private static string Key(string text) => new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // Without the prefixes "pfarrgemeinde", "pfarre", "urfahr" and "st" (for "St. Junia" / "Junia").
    private static string Short(string key)
    {
        foreach (var prefix in new[] { "pfarrgemeinde", "pfarre", "urfahr" })
            if (key.StartsWith(prefix)) key = key[prefix.Length..];
        return key == "stjunia" ? "junia" : key;
    }
}
