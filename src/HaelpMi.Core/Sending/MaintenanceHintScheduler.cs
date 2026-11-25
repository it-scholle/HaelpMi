namespace HaelpMi.Core.Sending;

/// <summary>
/// Meldet verzögert, dass der neutrale Wartungshinweis fällig ist. Mehrere Auslöser innerhalb
/// der Wartezeit ergeben nur einen Hinweis.
/// </summary>
public sealed class MaintenanceHintScheduler
{
    private readonly TimeSpan _delay;
    private readonly Func<TimeSpan, Task> _wait;
    private int _pending;

    /// <param name="wait">Nur für Tests ersetzbar; Standard ist <see cref="Task.Delay(TimeSpan)"/>.</param>
    public MaintenanceHintScheduler(TimeSpan delay, Func<TimeSpan, Task>? wait = null)
    {
        _delay = delay;
        _wait = wait ?? Task.Delay;
    }

    /// <summary>Wird nach Ablauf der Wartezeit auf einem Threadpool-Thread ausgelöst.</summary>
    public event EventHandler? Due;

    /// <returns>Die laufende Wartezeit; bei schon wartendem Hinweis eine erledigte Aufgabe.</returns>
    public Task Schedule()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return Task.CompletedTask;
        }

        return WaitAndRaiseAsync();
    }

    private async Task WaitAndRaiseAsync()
    {
        try
        {
            await _wait(_delay).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _pending, 0);
        }

        Due?.Invoke(this, EventArgs.Empty);
    }
}
