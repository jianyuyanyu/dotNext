using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static System.Threading.Timeout;

namespace DotNext.Threading.Tasks;

partial class ManualResetCompletionSource
{
    // With CancellationTokenSource.TryReset() it's not possible to reuse CTS if it's canceled.
    // For timeout-based async operations, timeouts can happen from time to time, which causes
    // allocation of the CTS every time when the completion source is reused. We want reuse the timer
    // even if it's fired.
    private sealed class Timer : IDisposable
    {
        [SuppressMessage("Performance", "CA1823", Justification = "False positive")]
        private const string TimerQueueTimerType = "System.Threading.TimerQueueTimer, System.Private.CoreLib";
        
        private readonly ITimer timer;
        private bool isCallbackCompleted;

        public Timer(TimerCallback callback, object? state)
        {
            callback += OnCallbackCompleted;
            timer = CreateTimer(callback, state, InfiniteTimeSpan, InfiniteTimeSpan);
        }

        private void OnCallbackCompleted(object? _) => Volatile.Write(ref isCallbackCompleted, true);

        public bool TryReset()
            => timer.Change(InfiniteTimeSpan, InfiniteTimeSpan) && TryResetCore();

        private bool TryResetCore()
        {
            ref var everQueued = ref Unsafe.NullRef<bool>();
            try
            {
                everQueued = ref IsEverQueued(timer);
            }
            catch (BadImageFormatException)
            {
                return false;
            }
            
            if (!everQueued)
            {
                // nothing to do
            }
            else if (Interlocked.TrueToFalse(ref isCallbackCompleted))
            {
                // timer callback was scheduled, we need to ensure that it's finished
                everQueued = false;
            }
            else
            {
                return false;
            }

            return true;
            
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_everQueued")]
            static extern ref bool IsEverQueued(
                [UnsafeAccessorType(TimerQueueTimerType)]
                object timer);
        }

        private static ITimer CreateTimer(TimerCallback timerCallback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ITimer? result;
            try
            {
                result = Create(timerCallback, state, dueTime, period, flowExecutionContext: false) as ITimer;
            }
            catch (BadImageFormatException)
            {
                result = null;
            }

            return result ?? TimeProvider.System.CreateTimer(timerCallback, state, dueTime, period);

            [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
            [return: UnsafeAccessorType(TimerQueueTimerType)]
            static extern object Create(TimerCallback timerCallback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period,
                bool flowExecutionContext);
        }

        public void Dispose() => timer.Dispose();

        public void Change(TimeSpan timeout) => timer.Change(timeout, InfiniteTimeSpan);
    }
}