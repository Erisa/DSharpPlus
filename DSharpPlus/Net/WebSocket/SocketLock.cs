using System;
using System.Threading;
using System.Threading.Tasks;

namespace DSharpPlus.Net.WebSocket;

// Licensed from Clyde.NET (etc; I don't know how licenses work)

internal sealed class SocketLock : IDisposable
{
    public ulong ApplicationId { get; }

    private SemaphoreSlim LockSemaphore { get; }
    private CancellationTokenSource TimeoutCancelSource { get; set; }
    private CancellationToken TimeoutCancel => this.TimeoutCancelSource.Token;
    private Task UnlockTask { get; set; }
    private int MaxConcurrency { get; set; }

    public SocketLock(ulong appId, int maxConcurrency)
    {
        this.ApplicationId = appId;
        this.TimeoutCancelSource = null;
        this.MaxConcurrency = maxConcurrency;
        this.LockSemaphore = new SemaphoreSlim(maxConcurrency);
    }

    public async Task LockAsync()
    {
        await this.LockSemaphore.WaitAsync();

        this.TimeoutCancelSource = new CancellationTokenSource();
        this.UnlockTask = Task.Delay(TimeSpan.FromSeconds(30), this.TimeoutCancel);
        _ = this.UnlockTask.ContinueWith(InternalUnlock, TaskContinuationOptions.NotOnCanceled);
    }

    public void UnlockAfter(TimeSpan unlockDelay)
    {
        if (this.TimeoutCancelSource == null || this.LockSemaphore.CurrentCount > 0)
        {
            return; // it's not unlockable because it's post-IDENTIFY or not locked
        }

        try
        {
            this.TimeoutCancelSource.Cancel();
            this.TimeoutCancelSource.Dispose();
        }
        catch { }
        this.TimeoutCancelSource = null;

        this.UnlockTask = Task.Delay(unlockDelay, CancellationToken.None);
        _ = this.UnlockTask.ContinueWith(InternalUnlock);
    }

    public Task WaitAsync()
        => this.LockSemaphore.WaitAsync();

    public void Dispose()
    {
        try
        {
            this.TimeoutCancelSource?.Cancel();
            this.TimeoutCancelSource?.Dispose();
        }
        catch { }
    }

    private void InternalUnlock(Task t)
        => this.LockSemaphore.Release(this.MaxConcurrency);
}
