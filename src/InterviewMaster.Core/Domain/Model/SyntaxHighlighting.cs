namespace InterviewMaster.Domain;

public enum SyntaxKind
{
    Plain,
    Keyword,
    Type,
    String,
    Number,
    Comment,
    Function,
    Decorator
}

public readonly record struct SyntaxRun(string Text, SyntaxKind Kind);

/// Splits code into coloured runs. The rules are the Mac app's `SyntaxHighlighter`; the interface
/// decides what colour each kind is.
public static class SyntaxHighlighter
{
    private static readonly Dictionary<string, HashSet<string>> Keywords = new()
    {
        ["python"] = new(StringComparer.Ordinal)
        {
            "def", "class", "if", "elif", "else", "for", "while", "return", "import", "from",
            "as", "try", "except", "finally", "raise", "with", "yield", "lambda", "pass",
            "break", "continue", "and", "or", "not", "in", "is", "async", "await", "None",
            "True", "False", "global", "nonlocal", "assert", "del"
        },
        ["java"] = new(StringComparer.Ordinal)
        {
            "public", "private", "protected", "class", "interface", "extends", "implements",
            "static", "final", "void", "int", "long", "double", "float", "boolean", "char",
            "byte", "short", "new", "return", "if", "else", "for", "while", "do", "switch",
            "case", "default", "break", "continue", "try", "catch", "finally", "throw",
            "throws", "import", "package", "this", "super", "null", "true", "false",
            "abstract", "synchronized", "volatile", "instanceof", "enum", "var", "record"
        },
        ["javascript"] = new(StringComparer.Ordinal)
        {
            "function", "const", "let", "var", "if", "else", "for", "while", "do",
            "switch", "case", "default", "break", "continue", "return", "try", "catch",
            "finally", "throw", "class", "extends", "new", "this", "super", "import",
            "export", "from", "as", "async", "await", "yield", "of", "in", "typeof",
            "instanceof", "delete", "void", "null", "undefined", "true", "false", "NaN"
        },
        ["sql"] = new(StringComparer.Ordinal)
        {
            "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "AND",
            "OR", "NOT", "IN", "IS", "NULL", "LIKE", "BETWEEN", "GROUP", "BY", "ORDER", "ASC",
            "DESC", "LIMIT", "OFFSET", "HAVING", "INSERT", "INTO", "VALUES", "UPDATE", "SET",
            "DELETE", "CREATE", "TABLE", "ALTER", "DROP", "INDEX", "VIEW", "DISTINCT", "AS",
            "CASE", "WHEN", "THEN", "ELSE", "END", "UNION", "ALL", "EXISTS", "COUNT", "SUM",
            "AVG", "MAX", "MIN", "COALESCE", "CAST", "select", "from", "where", "join", "and",
            "or", "not", "in", "is", "null", "like", "order", "by", "group", "having", "as"
        },
        ["swift"] = new(StringComparer.Ordinal)
        {
            "func", "class", "struct", "enum", "protocol", "extension", "var", "let", "if",
            "else", "guard", "for", "while", "repeat", "switch", "case", "default", "break",
            "continue", "return", "try", "catch", "throw", "throws", "defer", "import",
            "self", "Self", "super", "nil", "true", "false", "public", "private", "internal",
            "static", "final", "override", "mutating", "lazy", "weak", "async", "await"
        },
        ["go"] = new(StringComparer.Ordinal)
        {
            "func", "package", "import", "type", "struct", "interface", "var", "const", "if",
            "else", "for", "range", "switch", "case", "default", "break", "continue", "return",
            "go", "select", "chan", "map", "make", "new", "defer", "panic", "recover", "nil",
            "true", "false", "iota"
        }
    };

    private static readonly HashSet<string> GenericKeywords =
        new(Keywords["python"].Concat(Keywords["java"]).Concat(Keywords["javascript"]), StringComparer.Ordinal);

    private static readonly HashSet<string> BuiltinTypes = new(StringComparer.Ordinal)
    {
        "String", "Int", "Integer", "Long", "Double", "Float", "Boolean",
        "Bool", "List", "Array", "Dict", "Dictionary", "Map", "Set",
        "HashMap", "HashSet", "ArrayList", "Optional", "Result", "Void",
        "Object", "Number", "Date", "Error", "Exception", "Promise"
    };

    /// The runs of `code`, joined they are `code` again. `language` is one of python, java, javascript,
    /// sql, swift, go; null guesses it from the code.
    public static List<SyntaxRun> Highlight(string code, string? language = null)
    {
        var runs = new List<SyntaxRun>();
        if (code.Length == 0) return runs;

        var lang = (language ?? DetectLanguage(code)).ToLowerInvariant();
        var keywords = Keywords.TryGetValue(lang, out var known) ? known : GenericKeywords;
        var lines = code.Split('\n');
        var inComment = false;
        for (var index = 0; index < lines.Length; index++)
        {
            inComment = HighlightLine(lines[index], keywords, lang, inComment, runs);
            if (index < lines.Length - 1) Add(runs, "\n", SyntaxKind.Plain);
        }
        return runs;
    }

    public static string DetectLanguage(string code)
    {
        var lower = code.ToLowerInvariant();
        if (lower.Contains("def ") && lower.Contains(':')) return "python";
        if (lower.Contains("public class") || lower.Contains("public static void")) return "java";
        if (lower.Contains("function") || lower.Contains("const ") || lower.Contains("=>")) return "javascript";
        if (lower.Contains("select") && lower.Contains("from")) return "sql";
        if (lower.Contains("func ") && lower.Contains("->")) return "swift";
        if (lower.Contains("package ") && lower.Contains("func ")) return "go";
        return "generic";
    }

    private static void Add(List<SyntaxRun> runs, string text, SyntaxKind kind)
    {
        if (text.Length == 0) return;
        if (runs.Count > 0 && runs[^1].Kind == kind) runs[^1] = new SyntaxRun(runs[^1].Text + text, kind);
        else runs.Add(new SyntaxRun(text, kind));
    }

    /// Returns whether a block comment is still open at the end of the line.
    private static bool HighlightLine(string line, HashSet<string> keywords, string lang, bool inComment, List<SyntaxRun> runs)
    {
        if (line.Length == 0) return inComment;

        if (inComment)
        {
            var end = line.IndexOf("*/", StringComparison.Ordinal);
            if (end < 0)
            {
                Add(runs, line, SyntaxKind.Comment);
                return true;
            }
            Add(runs, line[..(end + 2)], SyntaxKind.Comment);
            HighlightLine(line[(end + 2)..], keywords, lang, false, runs);
            return false;
        }

        var commentStart = FindSingleLineComment(line, lang);
        var blockStart = line.IndexOf("/*", StringComparison.Ordinal);
        if (blockStart >= 0 && (commentStart is null || blockStart < commentStart))
        {
            HighlightTokens(line[..blockStart], keywords, runs);
            var after = line[blockStart..];
            if (after.Length > 2)
            {
                var end = after.IndexOf("*/", 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    Add(runs, after[..(end + 2)], SyntaxKind.Comment);
                    HighlightTokens(after[(end + 2)..], keywords, runs);
                    return false;
                }
            }
            Add(runs, after, SyntaxKind.Comment);
            return true;
        }

        if (commentStart is { } start)
        {
            HighlightTokens(line[..start], keywords, runs);
            Add(runs, line[start..], SyntaxKind.Comment);
            return false;
        }

        HighlightTokens(line, keywords, runs);
        return false;
    }

    private static int? FindSingleLineComment(string line, string lang)
    {
        var inString = false;
        var quote = '"';
        for (var index = 0; index < line.Length; index++)
        {
            var value = line[index];
            if (!inString && value is '"' or '\'')
            {
                if (!inString)
                {
                    inString = true;
                    quote = value;
                    continue;
                }
            }
            if (inString && value == quote)
            {
                if (index > 0 && line[index - 1] != '\\') inString = false;
                else continue;
            }
            if (inString) continue;
            var hasNext = index + 1 < line.Length;
            if (value == '/' && hasNext && line[index + 1] == '/') return index;
            if (value == '#' && lang == "python") return index;
            if (value == '-' && lang == "sql" && hasNext && line[index + 1] == '-') return index;
        }
        return null;
    }

    private static void HighlightTokens(string text, HashSet<string> keywords, List<SyntaxRun> runs)
    {
        var index = 0;
        while (index < text.Length)
        {
            var value = text[index];

            if (value is '"' or '\'')
            {
                var triple = index + 2 < text.Length && text[index + 1] == value && text[index + 2] == value;
                var end = triple ? TripleStringEnd(text, index, value) : StringEnd(text, index, value);
                Add(runs, text[index..end], SyntaxKind.String);
                index = end;
                continue;
            }

            if (value == '@')
            {
                var end = WordEnd(text, index + 1);
                Add(runs, text[index..end], SyntaxKind.Decorator);
                index = end;
                continue;
            }

            if (char.IsNumber(value) || (value == '.' && index + 1 < text.Length && char.IsNumber(text[index + 1])))
            {
                var end = NumberEnd(text, index);
                Add(runs, text[index..end], SyntaxKind.Number);
                index = end;
                continue;
            }

            if (char.IsLetter(value) || value == '_')
            {
                var end = WordEnd(text, index);
                var word = text[index..end];
                SyntaxKind kind;
                if (keywords.Contains(word)) kind = SyntaxKind.Keyword;
                else if (BuiltinTypes.Contains(word) || (char.IsUpper(word[0]) && word.Length > 1)) kind = SyntaxKind.Type;
                else if (end < text.Length && text[end] == '(') kind = SyntaxKind.Function;
                else kind = SyntaxKind.Plain;
                Add(runs, word, kind);
                index = end;
                continue;
            }

            Add(runs, value.ToString(), SyntaxKind.Plain);
            index++;
        }
    }

    private static int StringEnd(string text, int start, char quote)
    {
        for (var index = start + 1; index < text.Length; index++)
        {
            if (text[index] == quote && (index == start + 1 || text[index - 1] != '\\')) return index + 1;
        }
        return text.Length;
    }

    private static int TripleStringEnd(string text, int start, char quote)
    {
        for (var index = Math.Min(start + 3, text.Length); index < text.Length; index++)
        {
            if (text[index] != quote || index + 1 >= text.Length || text[index + 1] != quote) continue;
            if (index + 2 >= text.Length || text[index + 2] == quote) return Math.Min(index + 3, text.Length);
        }
        return text.Length;
    }

    private static int WordEnd(string text, int start)
    {
        var index = start;
        while (index < text.Length && (char.IsLetter(text[index]) || char.IsNumber(text[index]) || text[index] == '_')) index++;
        return index;
    }

    private static int NumberEnd(string text, int start)
    {
        var index = start;
        var hasDecimal = false;
        while (index < text.Length)
        {
            var value = text[index];
            if (char.IsNumber(value) || Uri.IsHexDigit(value))
            {
            }
            else if (value == '.' && !hasDecimal) hasDecimal = true;
            else if (value is 'x' or 'X' && index == start + 1 && text[start] == '0')
            {
            }
            else break;
            index++;
        }
        return index;
    }
}
