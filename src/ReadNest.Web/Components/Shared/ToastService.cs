namespace ReadNest.Web.Components.Shared;

public enum ToastKind
{
    Info,
    Success,
    Error,
}

/// <summary>A notification. <see cref="Undo"/> is offered for reversible actions.</summary>
public sealed record Toast(Guid Id, ToastKind Kind, string Message, Func<Task>? Undo);

/// <summary>Per-circuit notification queue rendered by <see cref="ToastHost"/>.</summary>
public sealed class ToastService
{
    private const int VisibleMilliseconds = 5000;
    private const int ErrorMilliseconds = 8000;

    private readonly List<Toast> _toasts = [];
    private readonly Lock _gate = new();

    public event Action? Changed;

    public IReadOnlyList<Toast> Current
    {
        get
        {
            lock (_gate)
            {
                return _toasts.ToList();
            }
        }
    }

    public void Success(string message, Func<Task>? undo = null) => Add(ToastKind.Success, message, undo);

    public void Error(string message) => Add(ToastKind.Error, message, null);

    public void Info(string message) => Add(ToastKind.Info, message, null);

    public void Dismiss(Guid id)
    {
        lock (_gate)
        {
            _toasts.RemoveAll(t => t.Id == id);
        }

        Changed?.Invoke();
    }

    /// <summary>Runs the toast's undo action once and removes it.</summary>
    public async Task UndoAsync(Guid id)
    {
        Toast? toast;
        lock (_gate)
        {
            toast = _toasts.FirstOrDefault(t => t.Id == id);
            _toasts.RemoveAll(t => t.Id == id);
        }

        Changed?.Invoke();
        if (toast?.Undo is { } undo)
        {
            await undo();
        }
    }

    private void Add(ToastKind kind, string message, Func<Task>? undo)
    {
        var toast = new Toast(Guid.NewGuid(), kind, message, undo);
        lock (_gate)
        {
            // One message at a time reads calmer; the newest replaces the previous one.
            _toasts.Clear();
            _toasts.Add(toast);
        }

        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id, kind == ToastKind.Error ? ErrorMilliseconds : VisibleMilliseconds);
    }

    private async Task DismissLaterAsync(Guid id, int milliseconds)
    {
        await Task.Delay(milliseconds);
        Dismiss(id);
    }
}
