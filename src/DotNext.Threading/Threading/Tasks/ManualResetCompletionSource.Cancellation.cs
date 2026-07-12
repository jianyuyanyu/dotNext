using System.Runtime.InteropServices;

namespace DotNext.Threading.Tasks;

partial class ManualResetCompletionSource
{
    private void CancellationRequested<TReason>(TReason reason)
        where TReason : struct, ICancellationReason, allows ref struct
    {
        if (BeginCompletion(reason.Version))
        {
            try
            {
                if (reason.TryGetToken(out var token))
                {
                    CompleteAsCanceled(token);
                }
                else
                {
                    CompleteAsTimedOut();
                }
            }
            finally
            {
                if (EndCompletion())
                {
                    NotifyConsumer<TReason>();
                }
            }
        }
    }
    
    private interface IResetOptions
    {
        public static abstract bool IsTimeout { get; }
        
        public static abstract bool IsCancellation { get; }
    }
    
    private interface ICancellationReason : IResetOptions
    {
        bool TryGetToken(out CancellationToken token);
        
        short Version { get; }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct ResetAllOptions : IResetOptions
    {
        static bool IResetOptions.IsTimeout => false;

        static bool IResetOptions.IsCancellation => false;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct CancellationReason(short version, CancellationToken cancellationToken) : ICancellationReason
    {
        static bool IResetOptions.IsTimeout => false;
        
        static bool IResetOptions.IsCancellation => true;

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
        static bool IResetOptions.IsTimeout => true;

        static bool IResetOptions.IsCancellation => false;

        bool ICancellationReason.TryGetToken(out CancellationToken token)
        {
            token = CancellationToken.None;
            return false;
        }
        
        short ICancellationReason.Version => version;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct DoNotResetOptions : IResetOptions
    {
        static bool IResetOptions.IsTimeout => true;

        static bool IResetOptions.IsCancellation => true;
    }
}