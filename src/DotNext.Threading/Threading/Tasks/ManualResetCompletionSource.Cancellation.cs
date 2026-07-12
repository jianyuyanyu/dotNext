using System.Runtime.InteropServices;

namespace DotNext.Threading.Tasks;

partial class ManualResetCompletionSource
{
    private void CancellationRequested<TReason>(TReason reason)
        where TReason : struct, ICancellationReason, allows ref struct
    {
        var runAsynchronously = runContinuationsAsynchronously;
        if (BeginCompletion(reason.Version))
        {
            try
            {
                if (reason.TryGetToken(out var token))
                {
                    CompleteAsCanceled(token);

                    // The executing callback cannot be unregistered (its node is already claimed by the CTS),
                    // so drop the registration to let Reset() reuse the cached version box and the timer.
                    // This also covers mass cancellation: when a single token cancels many sources, Reset()
                    // of an already-notified source can run while the token's callback list is still draining,
                    // so UnregisterAndReuse() would otherwise see the cancellation as in-flight and refuse reuse.
                    // This write is safe against Reset(): we're inside the Completing window, so ResetCore spins.
                    // It can race the field store in EnableCancellation if cancellation lands during activation;
                    // both torn outcomes are benign: (id, 0) reads as a default token, (0, node) fails Unregister
                    // with id == 0, and either way UnregisterAndReuse degrades to a conservative non-reuse.
                    tokenTracker = default;
                }
                else
                {
                    CompleteAsTimedOut();
                    runAsynchronously = true;
                }
            }
            finally
            {
                if (EndCompletion())
                {
                    // Without running continuation asynchronously, this method executes continuation
                    // which calls Reset() within the same thread. Reset() calls ResetCancellationState()
                    // which can't reset timeout/cancellation states correctly:
                    // 1. Timer.TryReset() always return false, because its second callback never reports that the timeout
                    // callback is finished
                    // 2. CancellationTokenRegistration.Unregister() returns false, because the current execution is
                    // a cancellation callback.
                    NotifyConsumer(ref continuation, runAsynchronously);
                }
            }
        }
    }
    
    private interface ICancellationReason
    {
        bool TryGetToken(out CancellationToken token);
        
        short Version { get; }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct CancellationReason(short version, CancellationToken cancellationToken) : ICancellationReason
    {
        bool ICancellationReason.TryGetToken(out CancellationToken token)
        {
            token = cancellationToken;
            return true;
        }

        short ICancellationReason.Version => version;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct TimeoutReason(short version) : ICancellationReason
    {
        bool ICancellationReason.TryGetToken(out CancellationToken token)
        {
            token = CancellationToken.None;
            return false;
        }
        
        short ICancellationReason.Version => version;
    }
}