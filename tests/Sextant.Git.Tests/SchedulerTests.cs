using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class SchedulerTests
{
    [Fact]
    public async Task Two_reads_overlap()
    {
        using var scheduler = new RepositoryScheduler();
        var started = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> Work(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref started) == 2)
                both.TrySetResult();
            return Wait(both, cancellationToken);
        }

        var first = scheduler.ReadAsync(Work, CancellationToken.None);
        var second = scheduler.ReadAsync(Work, CancellationToken.None);
        var completed = await Task.WhenAny(Task.WhenAll(first, second), Task.Delay(2000));
        Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully, "reads did not overlap");
        Assert.Same(completed, Task.WhenAll(first, second).ContinueWith(_ => completed, TaskScheduler.Default) is null ? completed : completed);
    }

    [Fact]
    public async Task Write_cancels_an_in_flight_read_then_runs()
    {
        for (var attempt = 0; attempt < 25; attempt++)
            await CancelInFlightRead();
    }

    [Fact]
    public async Task Read_that_wakes_as_a_write_ends_is_cancelled_by_the_next_write()
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            using var scheduler = new RepositoryScheduler();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writing = scheduler.WriteAsync(async cancellationToken =>
            {
                await release.Task.ConfigureAwait(false);
                return 1;
            }, CancellationToken.None);

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var read = scheduler.ReadAsync(token => HoldUntilCancelled(token, entered), CancellationToken.None);
            release.TrySetResult();
            Assert.Equal(1, await writing.WaitAsync(TimeSpan.FromSeconds(5)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var wrote = false;
            var next = scheduler.WriteAsync(cancellationToken =>
            {
                wrote = true;
                return Task.FromResult(2);
            }, CancellationToken.None);
            Assert.Equal(2, await next.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(wrote);
            await AssertCancelled(read);
        }
    }

    [Fact]
    public async Task Writes_do_not_overlap()
    {
        using var scheduler = new RepositoryScheduler();
        var current = 0;
        var max = 0;
        async Task<int> Work(CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref current);
            int seen;
            do
            {
                seen = max;
            }
            while (now > seen && Interlocked.CompareExchange(ref max, now, seen) != seen);

            await Task.Delay(40, cancellationToken);
            Interlocked.Decrement(ref current);
            return 1;
        }

        var all = Task.WhenAll(
            scheduler.WriteAsync(Work, CancellationToken.None),
            scheduler.WriteAsync(Work, CancellationToken.None),
            scheduler.WriteAsync(Work, CancellationToken.None));
        var finished = await Task.WhenAny(all, Task.Delay(3000));
        Assert.Same(all, finished);
        Assert.Equal(1, max);
    }

    private static async Task CancelInFlightRead()
    {
        using var scheduler = new RepositoryScheduler();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = scheduler.ReadAsync(token => HoldUntilCancelled(token, entered), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var wrote = false;
        var write = scheduler.WriteAsync(cancellationToken =>
        {
            wrote = true;
            return Task.FromResult(1);
        }, CancellationToken.None);

        Assert.Equal(1, await write.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(wrote);
        await AssertCancelled(read);
    }

    private static async Task<int> HoldUntilCancelled(CancellationToken cancellationToken, TaskCompletionSource entered)
    {
        // Register before announcing entry. Task.Delay(Timeout.Infinite) posts its
        // cancellation to the thread pool, and a busy Linux runner can miss a short wait.
        // Completing inline lets the read leave before the write waits. An asynchronous
        // continuation needs a thread-pool thread, which a busy runner may not hand out
        // before a short timeout.
        var cancelled = new TaskCompletionSource();
        using (cancellationToken.UnsafeRegister(static state => ((TaskCompletionSource)state!).TrySetResult(), cancelled))
        {
            entered.TrySetResult();
            await cancelled.Task.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return 0;
    }

    private static async Task AssertCancelled(Task<int> read)
    {
        try
        {
            await read.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("The read finished instead of being cancelled.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<int> Wait(TaskCompletionSource gate, CancellationToken cancellationToken)
    {
        await gate.Task.WaitAsync(cancellationToken);
        return 1;
    }
}
