namespace ThermoPrinterTool;

// One window owns downloads and the handoff to the updater, including manual checks.
internal sealed class UpdateSession
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private bool _busy;
    private bool _closed;
    private bool _applying;
    private string? _pending;

    public CancellationToken Token => _lifetime.Token;
    public bool IsBusy { get { lock (_sync) return _busy; } }

    public bool TryBeginAction()
    {
        lock (_sync)
        {
            if (_closed || _busy || _applying) return false;
            _busy = true;
            return true;
        }
    }

    public void EndAction() { lock (_sync) _busy = false; }

    public void Queue(string path)
    {
        lock (_sync)
            if (!_closed && !_applying) _pending = path;
    }

    public void ApplyPending(Action<string> apply)
    {
        lock (_sync)
        {
            if (_closed || _busy || _applying || _pending is null) return;
            var path = _pending;
            _pending = null;
            _applying = true;
            try { apply(path); }
            catch { _applying = false; throw; }
        }
    }

    public void Close()
    {
        lock (_sync) { _closed = true; _pending = null; }
        _lifetime.Cancel();
    }
}
