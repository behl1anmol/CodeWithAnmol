using System.Text.Json;

namespace NewsAggregator.Core.Application.Editorial;

/// <summary>
/// Pure, framework-free parser for the Editor agent's reply (docs §3.4). The Editor is
/// asked for a JSON object mapping each category to its section intro
/// (<c>{"AI":"…","Security":"…"}</c>); this extracts that map defensively. Like the
/// enrichment assembler (P1) it is deliberately <b>total</b>: markdown fences, prose
/// around the JSON, missing/blank/non-string values, or outright garbage never throw —
/// they just yield fewer (or no) intros, so the workflow falls back to <c>Intro = null</c>.
/// </summary>
public static class EditorIntroParser
{
    /// <summary>
    /// Parses <paramref name="raw"/> into a category → intro map. Lookups are
    /// case-insensitive so the section category resolves regardless of how the model cased
    /// the key. Null/blank keys and non-string or blank values are dropped; a later
    /// duplicate key wins. Never throws.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string? raw)
    {
        var intros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryExtractJsonObject(raw, out string json))
        {
            return intros;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return intros;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                // Only string values are intros; tolerate other kinds by skipping them so
                // one malformed entry can't discard the whole map.
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(property.Name) && !string.IsNullOrWhiteSpace(value))
                {
                    intros[property.Name.Trim()] = value.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON inside the extracted braces — keep whatever was parsed (none).
        }

        return intros;
    }

    /// <summary>
    /// Extracts the first balanced <c>{…}</c> object from arbitrary text, tolerating
    /// markdown code fences or prose around the JSON. String contents (and their escapes)
    /// are skipped so braces inside string values don't break brace matching.
    /// </summary>
    private static bool TryExtractJsonObject(string? text, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        json = text[start..(i + 1)];
                        return true;
                    }

                    break;
            }
        }

        return false;
    }
}
