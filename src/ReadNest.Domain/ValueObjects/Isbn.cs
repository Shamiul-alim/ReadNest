using System.Globalization;

namespace ReadNest.Domain.ValueObjects;

/// <summary>
/// ISBN normalisation and validation helpers. ISBNs are stored without separators; ISBN-10 values
/// are converted to ISBN-13 for duplicate detection because both forms identify the same edition.
/// </summary>
public static class Isbn
{
    /// <summary>Removes spaces and hyphens and upper-cases a trailing 'x'. Returns null for empty or non-ISBN input.</summary>
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 32)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[raw.Length];
        var length = 0;
        foreach (var c in raw)
        {
            if (char.IsAsciiDigit(c))
            {
                buffer[length++] = c;
            }
            else if (c is 'x' or 'X')
            {
                buffer[length++] = 'X';
            }
            else if (c is not ('-' or ' '))
            {
                return null;
            }
        }

        return length == 0 ? null : new string(buffer[..length]);
    }

    public static bool IsValidIsbn10(string? value)
    {
        if (value is not { Length: 10 })
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var c = value[i];
            int digit;
            if (char.IsAsciiDigit(c))
            {
                digit = c - '0';
            }
            else if (c == 'X' && i == 9)
            {
                digit = 10;
            }
            else
            {
                return false;
            }

            sum += digit * (10 - i);
        }

        return sum % 11 == 0;
    }

    public static bool IsValidIsbn13(string? value)
    {
        if (value is not { Length: 13 } || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        return CheckDigit13(value.AsSpan(0, 12)) == value[12] - '0';
    }

    /// <summary>Converts a valid ISBN-10 to its ISBN-13 (978 prefix) equivalent.</summary>
    public static string ToIsbn13(string isbn10)
    {
        if (!IsValidIsbn10(isbn10))
        {
            throw new ArgumentException("Not a valid ISBN-10.", nameof(isbn10));
        }

        var core = "978" + isbn10[..9];
        return core + CheckDigit13(core).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Converts a 978-prefixed ISBN-13 back to ISBN-10, or returns null when no ISBN-10 exists.</summary>
    public static string? ToIsbn10(string? isbn13)
    {
        if (!IsValidIsbn13(isbn13) || !isbn13!.StartsWith("978", StringComparison.Ordinal))
        {
            return null;
        }

        var core = isbn13.Substring(3, 9);
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (core[i] - '0') * (10 - i);
        }

        var check = (11 - (sum % 11)) % 11;
        return core + (check == 10 ? "X" : check.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Parses any ISBN-10/13 input into a canonical ISBN-13. Returns null when the input is not a valid ISBN.
    /// </summary>
    public static string? NormalizeToIsbn13(string? raw)
    {
        var cleaned = Clean(raw);
        return cleaned switch
        {
            { Length: 13 } when IsValidIsbn13(cleaned) => cleaned,
            { Length: 10 } when IsValidIsbn10(cleaned) => ToIsbn13(cleaned),
            _ => null,
        };
    }

    private static int CheckDigit13(ReadOnlySpan<char> first12)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (first12[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (10 - (sum % 10)) % 10;
    }
}
