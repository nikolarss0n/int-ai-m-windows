using System.Globalization;
using System.Text;

namespace InterviewMaster.Support;

/// String helpers that behave like the Swift standard library calls the macOS sources use, so each
/// ported function can be compared with its original line by line. Every comparison is ordinal.
internal static class SwiftText
{
    /// Swift `CharacterSet.whitespaces`: space separators and tab, not newlines.
    public static bool IsInlineWhitespace(char c) =>
        c == '\t' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

    /// Swift `trimmingCharacters(in: .whitespaces)`.
    public static string TrimInline(this string text) => text.TrimWhere(IsInlineWhitespace);

    /// Swift `trimmingCharacters(in: .punctuationCharacters)`.
    public static string TrimPunctuation(this string text) => text.TrimWhere(char.IsPunctuation);

    /// Swift `trimmingCharacters(in: CharacterSet(charactersIn:))`.
    public static string TrimChars(this string text, string characters) => text.Trim(characters.ToCharArray());

    public static string TrimWhere(this string text, Func<char, bool> shouldTrim)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && shouldTrim(text[start])) start++;
        while (end > start && shouldTrim(text[end - 1])) end--;
        return text.Substring(start, end - start);
    }

    public static bool HasPrefix(this string text, string prefix) => text.StartsWith(prefix, StringComparison.Ordinal);

    public static bool HasSuffix(this string text, string suffix) => text.EndsWith(suffix, StringComparison.Ordinal);

    /// Swift `prefix(_:)`: at most `count` characters from the start.
    public static string Prefix(this string text, int count) =>
        count >= text.Length ? text : text.Substring(0, Math.Max(0, count));

    /// Swift `suffix(_:)`: at most `count` characters from the end.
    public static string Suffix(this string text, int count) =>
        count >= text.Length ? text : text.Substring(text.Length - Math.Max(0, count));

    public static string DropFirst(this string text, int count = 1) =>
        count >= text.Length ? "" : text.Substring(Math.Max(0, count));

    public static string DropLast(this string text, int count = 1) =>
        count >= text.Length ? "" : text.Substring(0, text.Length - Math.Max(0, count));

    /// Swift `split(separator:)`: empty pieces are dropped.
    public static List<string> SplitOmittingEmpty(this string text, char separator) =>
        text.Split(separator, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// Swift `split(separator:omittingEmptySubsequences: false)` and `components(separatedBy:)`.
    public static List<string> SplitKeepingEmpty(this string text, char separator) => text.Split(separator).ToList();

    public static List<string> SplitKeepingEmpty(this string text, string separator) =>
        text.Split(separator, StringSplitOptions.None).ToList();

    /// Swift `split(whereSeparator:)`: empty pieces are dropped.
    public static List<string> SplitWhere(this string text, Func<char, bool> isSeparator)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (isSeparator(c))
            {
                if (current.Length > 0) pieces.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0) pieces.Add(current.ToString());
        return pieces;
    }

    /// The words of a text the way the Swift sources tokenize: runs of letters and digits.
    public static List<string> LetterNumberWords(this string text) =>
        text.SplitWhere(c => !char.IsLetter(c) && !char.IsNumber(c));

    /// Swift `filter` over a string's characters.
    public static string Where(this string text, Func<char, bool> keep)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text) if (keep(c)) builder.Append(c);
        return builder.ToString();
    }

    /// Swift maps "İ" to "i" plus a combining dot and "ß" to "SS"; .NET's invariant mapping leaves both alone.
    public static string Lowercased(this string text)
    {
        var lowered = text.ToLowerInvariant();
        return lowered.Contains('\u0130') ? lowered.Replace("\u0130", "i\u0307") : lowered;
    }

    public static string Uppercased(this string text)
    {
        var raised = text.ToUpperInvariant();
        return raised.Contains('ß') ? raised.Replace("ß", "SS") : raised;
    }

    public static bool ContainsAny(this string text, IEnumerable<string> needles)
    {
        foreach (var needle in needles) if (text.Contains(needle, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsDisjoint<T>(this IEnumerable<T> first, IEnumerable<T> second)
    {
        var set = first as ISet<T> ?? new HashSet<T>(first);
        foreach (var item in second) if (set.Contains(item)) return false;
        return true;
    }
}

/// A list compared by its contents, standing in for a Swift array inside an `Equatable` struct.
[System.Text.Json.Serialization.JsonConverter(typeof(ValueListJsonConverterFactory))]
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    public static readonly ValueList<T> Empty = new(Array.Empty<T>());

    private readonly T[] items;

    public ValueList(IEnumerable<T> items) => this.items = items.ToArray();

    public T this[int index] => items[index];

    public int Count => items.Length;

    public bool IsEmpty => items.Length == 0;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => items.GetEnumerator();

    public bool Equals(ValueList<T>? other) => other is not null && items.SequenceEqual(other.items);

    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in items) hash.Add(item);
        return hash.ToHashCode();
    }

    public static implicit operator ValueList<T>(T[] items) => new(items);

    public static implicit operator ValueList<T>(List<T> items) => new(items);
}
