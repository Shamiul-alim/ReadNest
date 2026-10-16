using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ReadNest.Application.Common;
using ReadNest.Domain.Exceptions;
using ReadNest.Web.Authorization;
using ReadNest.Web.Components.Shared;

namespace ReadNest.Web.Components;

/// <summary>
/// Base class for interactive pages: resolves the signed-in user from the server-side authentication state and
/// turns expected failures into friendly notifications instead of breaking the circuit.
/// </summary>
public abstract class ReadNestComponentBase : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _disposed = new();

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    [Inject]
    protected ToastService Toasts { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ILoggerFactory LoggerFactory { get; set; } = default!;

    /// <summary>Cancelled when the component is disposed (navigation away or circuit closed).</summary>
    protected CancellationToken ComponentToken => _disposed.Token;

    protected bool IsBusy { get; private set; }

    protected async Task<Guid?> GetUserIdAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return null;
        }

        var state = await AuthenticationStateTask;
        return state.User.TryGetUserId();
    }

    protected async Task<Guid> RequireUserIdAsync() =>
        await GetUserIdAsync() ?? throw new UnauthorizedAccessException("Please sign in again.");

    /// <summary>Runs a user action with busy tracking. Returns true on success; failures are shown as toasts.</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string? successMessage = null)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            await action();
            if (successMessage is not null)
            {
                Toasts.Success(successMessage);
            }

            return true;
        }
        catch (Exception ex) when (Describe(ex) is { } message)
        {
            Toasts.Error(message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Loads data for display. Returns the error message to show, or null on success.</summary>
    protected async Task<string?> LoadAsync(Func<Task> loader)
    {
        try
        {
            await loader();
            return null;
        }
        catch (Exception ex) when (Describe(ex) is { } message)
        {
            return message;
        }
    }

    /// <summary>Maps exceptions to user-facing messages; null means "ignore" (the component was disposed).</summary>
    protected string? Describe(Exception exception)
    {
        switch (exception)
        {
            case OperationCanceledException when _disposed.IsCancellationRequested:
                return null;
            case RequestValidationException validation:
                return string.Join(" ", validation.Errors.SelectMany(e => e.Value).Distinct());
            case DomainException or AppException:
                return exception.Message;
            case UnauthorizedAccessException:
                return "Your session has expired. Please sign in again.";
            default:
                LoggerFactory.CreateLogger(GetType()).LogError(exception, "Unexpected error in {Component}", GetType().Name);
                return "Something went wrong. Please try again.";
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        // Cancel only: the token may still be observed by in-flight work after disposal.
        if (disposing && !_disposed.IsCancellationRequested)
        {
            _disposed.Cancel();
        }
    }
}
