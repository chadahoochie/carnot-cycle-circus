using CarnotCycleCircus.Core.Domain.Learning;
using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AutonomousSelfImprovementWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_WhenAutoRunOnStartupEnabled_ShouldWaitTwoSecondsAndRunStartupCycle()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = true,
            SelfImprovementIntervalSeconds = 60
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = worker.ExecuteWorkerAsync(cts.Token);

        // Before 2s delay expires, no cycle should have run
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        fakeEngine.CallCount.Should().Be(0);

        // After advancing past 2s startup delay, initial cycle should run
        timeProvider.Advance(TimeSpan.FromSeconds(1.1));
        fakeEngine.CallCount.Should().Be(1);

        // Cancel and finish
        await cts.CancelAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(60));
        await executeTask;

        // Assert
        fakeEngine.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAutoRunOnStartupDisabled_ShouldSkipStartupCycleAndDelayUntilInterval()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = false,
            SelfImprovementIntervalSeconds = 30
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = worker.ExecuteWorkerAsync(cts.Token);

        // After 2s, still 0 because startup run is disabled
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        fakeEngine.CallCount.Should().Be(0);

        // After 29s total, still 0
        timeProvider.Advance(TimeSpan.FromSeconds(27));
        fakeEngine.CallCount.Should().Be(0);

        // After 30s interval expires, first periodic cycle runs
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        fakeEngine.CallCount.Should().Be(1);

        // Cancel and cleanup
        await cts.CancelAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        await executeTask;

        fakeEngine.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledDuringStartupDelay_ShouldExitGracefullyWithoutRunningCycle()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = true,
            SelfImprovementIntervalSeconds = 60
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = worker.ExecuteWorkerAsync(cts.Token);

        // Advance only 1 second into the 2-second delay
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await cts.CancelAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        // Await completion - should return cleanly without throwing unhandled exception
        var act = async () => await executeTask;
        await act.Should().NotThrowAsync();

        // Assert
        fakeEngine.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStartupCycleThrowsTransientException_ShouldSwallowAndProceedToPeriodicLoop()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        fakeEngine.SetException(new InvalidOperationException("Transient startup initialization failure"));

        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = true,
            SelfImprovementIntervalSeconds = 20
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = worker.ExecuteWorkerAsync(cts.Token);

        // Advance past 2s startup delay - initial cycle triggers and throws, swallowed by catch
        timeProvider.Advance(TimeSpan.FromSeconds(2.1));
        fakeEngine.CallCount.Should().Be(1);

        // Worker should continue into periodic loop; advance past 20s interval
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        fakeEngine.CallCount.Should().Be(2);

        // Cancel and cleanup
        await cts.CancelAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        await executeTask;

        fakeEngine.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStartupCycleThrowsOperationCanceledException_WhenCancellationRequested_ShouldExit()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        using var cts = new CancellationTokenSource();

        fakeEngine.SetCallback(token =>
        {
            cts.Cancel();
            throw new OperationCanceledException(token);
        });

        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = true,
            SelfImprovementIntervalSeconds = 60
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);

        // Act
        var executeTask = worker.ExecuteWorkerAsync(cts.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(2.1));

        var act = async () => await executeTask;
        await act.Should().NotThrowAsync();

        // Assert
        fakeEngine.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_PeriodicLoop_ShouldRunCycleAtEachInterval()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = true,
            SelfImprovementIntervalSeconds = 15
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = worker.ExecuteWorkerAsync(cts.Token);

        // Startup run
        timeProvider.Advance(TimeSpan.FromSeconds(2.1));
        fakeEngine.CallCount.Should().Be(1);

        // Cycle 1: +15s
        timeProvider.Advance(TimeSpan.FromSeconds(15));
        fakeEngine.CallCount.Should().Be(2);

        // Cycle 2: +15s
        timeProvider.Advance(TimeSpan.FromSeconds(15));
        fakeEngine.CallCount.Should().Be(3);

        // Cycle 3: +15s
        timeProvider.Advance(TimeSpan.FromSeconds(15));
        fakeEngine.CallCount.Should().Be(4);

        // Cancel
        await cts.CancelAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(15));
        await executeTask;

        fakeEngine.CallCount.Should().Be(4);
    }

    [Fact]
    public async Task ExecuteAsync_PeriodicLoop_WithIntervalBelowTenSeconds_ShouldClampToTenSeconds()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = false,
            SelfImprovementIntervalSeconds = 3 // Below minimum of 10s
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = worker.ExecuteWorkerAsync(cts.Token);

        // Advance 5 seconds - should NOT have executed yet (because interval is clamped to 10s)
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        fakeEngine.CallCount.Should().Be(0);

        // Advance another 5.1 seconds - 10s boundary reached, cycle executes
        timeProvider.Advance(TimeSpan.FromSeconds(5.1));
        fakeEngine.CallCount.Should().Be(1);

        // Cancel and cleanup
        await cts.CancelAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await executeTask;

        fakeEngine.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_PeriodicLoop_WhenCycleThrowsException_ShouldSwallowAndContinueNextCycle()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = false,
            SelfImprovementIntervalSeconds = 10
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();

        var executeTask = worker.ExecuteWorkerAsync(cts.Token);

        // First tick throws an error
        fakeEngine.SetException(new TimeoutException("Temporary storage backend timeout"));
        timeProvider.Advance(TimeSpan.FromSeconds(10.1));
        fakeEngine.CallCount.Should().Be(1);

        // Second tick succeeds
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        fakeEngine.CallCount.Should().Be(2);

        // Cancel
        await cts.CancelAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await executeTask;

        fakeEngine.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_PeriodicLoop_WhenCancelledDuringLoopDelay_ShouldBreakLoopAndExit()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = false,
            SelfImprovementIntervalSeconds = 20
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();

        var executeTask = worker.ExecuteWorkerAsync(cts.Token);

        // Advance 10s into 20s interval
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        fakeEngine.CallCount.Should().Be(0);

        // Cancel token during wait
        await cts.CancelAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(20));

        var act = async () => await executeTask;
        await act.Should().NotThrowAsync();
        fakeEngine.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_PeriodicLoop_WhenCancelledDuringCycleExecution_ShouldBreakLoopAndExit()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        using var cts = new CancellationTokenSource();

        fakeEngine.SetCallback(token =>
        {
            cts.Cancel();
            throw new OperationCanceledException(token);
        });

        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = false,
            SelfImprovementIntervalSeconds = 10
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);

        var executeTask = worker.ExecuteWorkerAsync(cts.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(10.1));

        var act = async () => await executeTask;
        await act.Should().NotThrowAsync();
        fakeEngine.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithPreCancelledToken_ShouldExitImmediately()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = true,
            SelfImprovementIntervalSeconds = 10
        };

        var worker = new TestableAutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var executeTask = worker.ExecuteWorkerAsync(cts.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(20));

        // Assert
        var act = async () => await executeTask;
        await act.Should().NotThrowAsync();
        fakeEngine.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Worker_StartAsyncAndStopAsync_ShouldManageLifecycleCorrectly()
    {
        // Arrange
        var fakeEngine = new FakeSelfImprovementEngine();
        var timeProvider = new TestTimeProvider();
        var options = new CarnotStorageOptions
        {
            AutoRunSelfImprovementOnStartup = true,
            SelfImprovementIntervalSeconds = 10
        };

        var worker = new AutonomousSelfImprovementWorker(fakeEngine, options, timeProvider);

        // Act - Start background service
        await worker.StartAsync(CancellationToken.None);

        // Wait until startup delay timer is registered
        await timeProvider.WaitForTimerAsync();

        // Advance past startup delay
        timeProvider.Advance(TimeSpan.FromSeconds(2.1));

        // Wait until periodic loop timer is registered
        await timeProvider.WaitForTimerAsync();
        fakeEngine.CallCount.Should().Be(1);

        // Advance past periodic interval
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await timeProvider.WaitForTimerAsync();
        fakeEngine.CallCount.Should().Be(2);

        // Act - Stop background service gracefully
        await worker.StopAsync(CancellationToken.None);

        // Assert worker stopped cleanly without additional executions
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        fakeEngine.CallCount.Should().Be(2);
    }

    [Fact]
    public void ServiceCollectionExtensions_AddCarnotCycleCircusCore_ShouldRegisterWorkerAsHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCarnotCycleCircusCore();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var hostedServices = serviceProvider.GetServices<IHostedService>();

        // Assert
        hostedServices.Should().ContainSingle(s => s is AutonomousSelfImprovementWorker);
    }

    private sealed class TestableAutonomousSelfImprovementWorker(
        ISelfImprovementEngine engine,
        CarnotStorageOptions options,
        TimeProvider? timeProvider = null)
        : AutonomousSelfImprovementWorker(engine, options, timeProvider)
    {
        public Task ExecuteWorkerAsync(CancellationToken stoppingToken) => ExecuteAsync(stoppingToken);
    }

    private sealed class FakeSelfImprovementEngine : ISelfImprovementEngine
    {
        private int _callCount;
        private readonly List<CancellationToken> _tokensReceived = [];
        private Exception? _exceptionToThrow;
        private Func<CancellationToken, Task>? _customCallback;

        public int CallCount => Volatile.Read(ref _callCount);
        public IReadOnlyList<CancellationToken> TokensReceived
        {
            get
            {
                lock (_tokensReceived)
                {
                    return [.. _tokensReceived];
                }
            }
        }

        public event Action<SelfImprovementReport>? OnSelfImprovementCompleted;

        public void SetException(Exception? ex) => _exceptionToThrow = ex;
        public void SetCallback(Func<CancellationToken, Task>? callback) => _customCallback = callback;

        public SelfImprovementReport GetLatestReport() =>
            new(
                TotalCyclesRun: _callCount,
                InsightsDistilledCount: 0,
                ProceduralRecipesGenerated: 0,
                SemanticRulesReinforced: 0,
                MemoriesConsolidatedCount: 0,
                DecayedMemoriesPrunedCount: 0,
                DistilledInsights: Array.Empty<string>(),
                Timestamp: DateTimeOffset.UtcNow
            );

        public async Task<SelfImprovementReport> RunSelfImprovementCycleAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            lock (_tokensReceived)
            {
                _tokensReceived.Add(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_exceptionToThrow != null)
            {
                var ex = _exceptionToThrow;
                _exceptionToThrow = null;
                throw ex;
            }

            if (_customCallback != null)
            {
                await _customCallback(cancellationToken);
            }

            var report = GetLatestReport();
            OnSelfImprovementCompleted?.Invoke(report);
            return report;
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
        private readonly List<TestTimer> _timers = [];
        private readonly object _lock = new();

        public int ActiveTimersCount
        {
            get
            {
                lock (_lock) return _timers.Count;
            }
        }

        public async Task WaitForTimerAsync(TimeSpan timeout = default)
        {
            if (timeout == default) timeout = TimeSpan.FromSeconds(2);
            var start = global::System.Diagnostics.Stopwatch.GetTimestamp();
            while (ActiveTimersCount == 0)
            {
                if (global::System.Diagnostics.Stopwatch.GetElapsedTime(start) > timeout)
                {
                    throw new TimeoutException("Timed out waiting for timer registration.");
                }
                await Task.Delay(5);
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock) return _utcNow;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_lock)
            {
                var timer = new TestTimer(this, callback, state, dueTime, period);
                _timers.Add(timer);
                if (dueTime == TimeSpan.Zero)
                {
                    timer.Trigger();
                }
                return timer;
            }
        }

        public void Advance(TimeSpan delta)
        {
            List<TestTimer> toTrigger = [];
            lock (_lock)
            {
                _utcNow += delta;
                foreach (var timer in _timers.ToArray())
                {
                    if (timer.CheckDue(_utcNow))
                    {
                        toTrigger.Add(timer);
                    }
                }
            }

            foreach (var timer in toTrigger)
            {
                timer.Trigger();
            }
        }

        private void RemoveTimer(TestTimer timer)
        {
            lock (_lock)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class TestTimer : ITimer
        {
            private readonly TestTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private DateTimeOffset _dueTime;
            private TimeSpan _period;
            private bool _disposed;

            public TestTimer(TestTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                _dueTime = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner._utcNow + dueTime;
                _period = period;
            }

            public bool CheckDue(DateTimeOffset now)
            {
                return !_disposed && now >= _dueTime;
            }

            public void Trigger()
            {
                if (_disposed) return;
                if (_period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan)
                {
                    _dueTime += _period;
                }
                else
                {
                    _dueTime = DateTimeOffset.MaxValue;
                }
                _callback(_state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                _dueTime = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : _owner._utcNow + dueTime;
                _period = period;
                return true;
            }

            public void Dispose()
            {
                _disposed = true;
                _owner.RemoveTimer(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
