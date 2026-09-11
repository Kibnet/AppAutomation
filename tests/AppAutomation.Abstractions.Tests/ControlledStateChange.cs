namespace AppAutomation.Abstractions.Tests;

/// <summary>Publishes a state change on a dedicated worker after the operation observes the old state.</summary>
internal static class ControlledStateChange
{
    public static async Task RunAsync(Action<Action> operation, Action changeState)
    {
        using var observed = new ManualResetEventSlim();
        using var ready = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var worker = Task.Factory.StartNew(
            () =>
            {
                ready.Set();
                try
                {
                    observed.Wait(cancellation.Token);
                    changeState();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            if (!ready.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The state-change worker did not start.");
            }

            operation(observed.Set);
        }
        finally
        {
            cancellation.Cancel();
            await worker;
        }
    }
}
