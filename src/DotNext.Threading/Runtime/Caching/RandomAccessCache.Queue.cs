using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DotNext.Runtime.Caching;

public partial class RandomAccessCache<TKey, TValue>
{
    // Queue has multiple producers and a single consumer. Consumer doesn't require special lock-free approach to dequeue.
    private KeyValuePair queueTail, queueHead;

    private void Promote(KeyValuePair newPair)
    {
        KeyValuePair currentTail;
        do
        {
            currentTail = queueTail;
        } while (Interlocked.CompareExchange(ref currentTail.NextInQueue, newPair, null) is not null);

        // attempt to install a new tail. Do not retry if failed, competing thread installed more recent version of it
        Interlocked.CompareExchange(ref queueTail, newPair, currentTail);

        currentTail.TrySetResult();
    }

    partial class KeyValuePair
    {
        // null, or KeyValuePair, or Sentinel.Instance
        internal object? NextInQueue;
        
        [ExcludeFromCodeCoverage]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal int QueueLength
        {
            get
            {
                var count = 0;
                for (var current = this; current is not null; current = current.NextInQueue as KeyValuePair)
                {
                    count++;
                }

                return count;
            }
        }
    }

    // Never call GetValue on this class, it has no storage for TValue.
    // It is used as a stub for the first element in the notification queue to keep task completion source
    private sealed class FakeKeyValuePair() : KeyValuePair(default!, 0)
    {
        public override string ToString() => "Fake KV Pair";
    }
}