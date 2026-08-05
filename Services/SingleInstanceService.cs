namespace SimpitLauncher.Services;

/// <summary>
/// Ensures only one interactive UI instance runs. CLI --start/--stop launches bypass this.
/// </summary>
public static class SingleInstanceService
{
    private const string MutexName = @"Local\Stuart.SimpitLauncher.SingleInstance";
    private const string ActivateEventName = @"Local\Stuart.SimpitLauncher.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static CancellationTokenSource? _listenCts;

    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: false, MutexName);
            try
            {
                return _mutex.WaitOne(0, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                // Previous owner crashed; we now hold the mutex.
                return true;
            }
        }
        catch
        {
            // Fail open so a mutex problem does not brick the app.
            return true;
        }
    }

    public static void SignalActivate()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ActivateEventName);
            ev.Set();
        }
        catch
        {
            // Existing instance may not be listening yet.
        }
    }

    public static void StartActivateListener(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _listenCts = new CancellationTokenSource();
        var token = _listenCts.Token;

        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_activateEvent.WaitOne(TimeSpan.FromMilliseconds(500)))
                    {
                        onActivate();
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }
        }, token);
    }
}
