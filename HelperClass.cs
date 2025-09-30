using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal readonly record struct InventoryChange<TKey, TAmount>(
    IReadOnlyList<KeyValuePair<TKey, TAmount>> ChangeList,
    TaskCompletionSource<bool>? Response = null
);

internal class Constants
{
    internal const string BaseVersion = "V1.0";
}

internal class AsyncBooleanGate
{
    private readonly object _lock = new();
    private TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Wait() => _tcs.Task;

    public void Open()
    {
        lock (_lock)
        {
            _tcs.TrySetResult(true);
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            if (!_tcs.Task.IsCompleted) { return; }
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}