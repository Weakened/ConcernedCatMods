using System.Runtime.ExceptionServices;

namespace ConcernedTeamster.Tests;

/// <summary>
/// Measures allocations on a dedicated thread after a full iteration warmup.
/// Keeping the measured region off xUnit's worker prevents unrelated runner
/// work from being charged to the product code under test. A dedicated thread
/// alone does not stop collections set off by other threads from charging it
/// (#469, observed in #463), so the measured loop runs in an
/// <see cref="ExactAllocations"/> window, and every caller sits in
/// <see cref="ExactAllocationCollection"/>.
/// </summary>
internal static class AllocationProbe
{
    public static long MeasureAfterWarmup(
        Action operation,
        int warmupIterations,
        int measuredIterations)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return MeasureAfterWarmup(
            () => operation,
            warmupIterations,
            measuredIterations);
    }

    public static long MeasureAfterWarmup(
        Func<Action> operationFactory,
        int warmupIterations,
        int measuredIterations)
    {
        ArgumentNullException.ThrowIfNull(operationFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warmupIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measuredIterations);

        long allocated = -1;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Action operation = operationFactory();
                for (int index = 0; index < warmupIterations; index++)
                {
                    operation();
                }

                operation = operationFactory();
                Action measured = operation;
                allocated = ExactAllocations.Measure(() =>
                {
                    for (int index = 0; index < measuredIterations; index++)
                    {
                        measured();
                    }
                });
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "Teamster allocation probe",
        };

        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return allocated;
    }
}
