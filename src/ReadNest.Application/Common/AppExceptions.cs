using ReadNest.Domain.Exceptions;

namespace ReadNest.Application.Common;

/// <summary>Base type for expected application failures. Messages are safe to show to users.</summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>
/// The item does not exist <em>or</em> belongs to another user. Both cases deliberately look identical so
/// callers cannot probe for other users' private records.
/// </summary>
public sealed class NotFoundException(string message = "The requested item was not found.") : AppException(message);

public sealed class ConflictException(string message) : AppException(message);

public sealed class ServiceUnavailableException(string message) : AppException(message);

public sealed class RequestValidationException : AppException
{
    public RequestValidationException(string field, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }

    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static RequestValidationException From(DomainException exception) =>
        new(exception.Field ?? "request", exception.Message);
}
