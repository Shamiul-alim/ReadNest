using ReadNest.Domain.Exceptions;

namespace ReadNest.Domain.Common;

public static class Guard
{
    public static string Required(string? value, int maxLength, string field, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException($"{label} is required.", field);
        }

        return MaxLength(trimmed, maxLength, field, label);
    }

    public static string? Optional(string? value, int maxLength, string field, string label)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : MaxLength(trimmed, maxLength, field, label);
    }

    public static int Range(int value, int min, int max, string field, string label)
    {
        if (value < min || value > max)
        {
            throw new DomainException($"{label} must be between {min} and {max}.", field);
        }

        return value;
    }

    public static DateOnly NotInFuture(DateOnly value, DateOnly today, string field, string label)
    {
        if (value > today)
        {
            throw new DomainException($"{label} cannot be in the future.", field);
        }

        if (value.Year < 1900)
        {
            throw new DomainException($"{label} is not a valid date.", field);
        }

        return value;
    }

    private static string MaxLength(string value, int maxLength, string field, string label)
    {
        if (value.Length > maxLength)
        {
            throw new DomainException($"{label} must be at most {maxLength} characters.", field);
        }

        return value;
    }
}
