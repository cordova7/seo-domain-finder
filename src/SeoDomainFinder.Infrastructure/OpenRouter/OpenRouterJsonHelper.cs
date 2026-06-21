namespace SeoDomainFinder.Infrastructure.OpenRouter;

internal static class OpenRouterJsonHelper
{
    public static string StripMarkdown(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("```"))
            return text;

        var lines = text.Split('\n').Skip(1);
        return string.Join('\n', lines).TrimEnd('`', '\n', ' ');
    }

    public static string? ExtractJsonObject(string text)
    {
        text = StripMarkdown(text);
        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)];
            }
        }

        return null;
    }

    public static string? ExtractJsonArray(string text)
    {
        text = StripMarkdown(text);
        var start = text.IndexOf('[');
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '[')
                depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)];
            }
        }

        return null;
    }
}
