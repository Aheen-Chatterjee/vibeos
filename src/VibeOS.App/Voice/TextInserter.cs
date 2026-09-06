using VibeOS.App.Actions;
using VibeOS.App.Windows;

namespace VibeOS.App.Voice;

/// <summary>
/// Inserts dictated text at the caret via clipboard paste (voice plan §6):
/// save clipboard → set transcript → Ctrl+V → restore. Works in editors,
/// browsers, chat, Windows Terminal and conhost. Runs on its own STA thread
/// (WinForms clipboard requirement); SendInput itself is thread-safe.
/// </summary>
public sealed class TextInserter : IDisposable
{
    private readonly SendInputInjector _injector;
    private readonly Action<string> _log;
    private readonly Queue<InsertJob> _queue = new();
    private readonly ManualResetEventSlim _signal = new(false);
    private readonly Thread _thread;
    private bool _disposed;

    private sealed record InsertJob(string Text, KeyGesture? Submit);

    public TextInserter(SendInputInjector injector, Action<string> log)
    {
        _injector = injector;
        _log = log;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "VibeOS.Inserter",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Insert(string text, KeyGesture? submit = null)
    {
        if (string.IsNullOrEmpty(text) || _disposed) return;
        lock (_queue) _queue.Enqueue(new InsertJob(text, submit));
        _signal.Set();
    }

    private void Loop()
    {
        while (!_disposed)
        {
            _signal.Wait();
            if (_disposed) return;
            InsertJob job;
            lock (_queue)
            {
                if (_queue.Count == 0)
                {
                    _signal.Reset();
                    continue;
                }
                job = _queue.Dequeue();
                if (_queue.Count == 0) _signal.Reset();
            }
            try { Paste(job); }
            catch (Exception ex) { _log($"[VibeOS] Insert failed: {ex.Message}"); }
        }
    }

    private void Paste(InsertJob job)
    {
        string? previous = null;
        try { previous = Clipboard.GetText(); } catch { }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(job.Text);
                break;
            }
            catch { Thread.Sleep(50); }
        }

        _injector.SendChord(new[] { VirtualKey.Control }, VirtualKey.V);
        Thread.Sleep(250);

        if (previous is not null)
        {
            try { Clipboard.SetText(previous); } catch { }
        }

        if (job.Submit is not null)
        {
            Thread.Sleep(150);
            if (job.Submit.Modifiers.Count == 0) _injector.KeyTap(job.Submit.Key);
            else _injector.SendChord(job.Submit.Modifiers, job.Submit.Key);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _signal.Set();
        _signal.Dispose();
    }
}
