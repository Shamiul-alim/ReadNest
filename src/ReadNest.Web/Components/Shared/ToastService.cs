namespace ReadNest.Web.Components.Shared;

public enum ToastKind
{
    Info,
    Success,
    Error,
}

public sealed record Toast(Guid Id, ToastKind Kind, string Message);

/// <summary>Per-circuit notification queue rendered by <see cref="ToastHost"/>.</summary>
public sealed class ToastService
{
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

    public void Success(string message) => Add(ToastKind.Success, message);

    public void Error(string message) => Add(ToastKind.Error, message);

    public void Info(string message) => Add(ToastKind.Info, message);

    public void Dismiss(Guid id)
    {
        lock (_gate)
        {
            _toasts.RemoveAll(t => t.Id == id);
        }

        Changed?.Invoke();
    }

    private void Add(ToastKind kind, string message)
    {
        var toast = new Toast(Guid.NewGuid(), kind, message);
        lock (_gate)
        {
            _toasts.Add(toast);
            if (_toasts.Count > 4)
            {
                _toasts.RemoveAt(0);
            }
        }

        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id, kind == ToastKind.Error ? 8000 : 4500);
    }

    private async Task DismissLaterAsync(Guid id, int milliseconds)
    {
        await Task.Delay(milliseconds);
        Dismiss(id);
    }
}
