namespace ReadNest.Domain.Exceptions;

/// <summary>
/// Raised when an operation would violate a domain invariant (for example an impossible page range).
/// The message is safe to show to the user; <see cref="Field"/> optionally names the offending input.
/// </summary>
public sealed class DomainException(string message, string? field = null) : Exception(message)
{
    public string? Field { get; } = field;
}
