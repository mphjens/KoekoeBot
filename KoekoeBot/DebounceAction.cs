
using System;
using System.Threading;
using System.Threading.Tasks;

public struct DebouncedAction
{
    public readonly Action action;
    public readonly CancellationTokenSource cts;

    public DebouncedAction(Action action, CancellationTokenSource cts)
    {
        this.action = action;
        this.cts = cts;
    }
}

public static class DebounceAction
{
    public static DebouncedAction Debounce(this Action func, int milliseconds = 300)
    {
        var tokenSource = new CancellationTokenSource();
        var last = 0;
        return new DebouncedAction(
            () =>
            {
                var current = Interlocked.Increment(ref last);
                Task.Delay(milliseconds, tokenSource.Token).ContinueWith(task =>
                {
                    if (current == last) func();
                    task.Dispose();
                });
            }
        , tokenSource);
    }
}
