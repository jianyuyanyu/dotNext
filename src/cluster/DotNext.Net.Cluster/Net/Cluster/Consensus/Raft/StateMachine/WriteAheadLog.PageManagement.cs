using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext.Collections.Concurrent;

namespace DotNext.Net.Cluster.Consensus.Raft.StateMachine;

using Buffers;
using Collections.Generic;

partial class WriteAheadLog
{
    private static int GetPageOffset(ulong address, int pageSize)
    {
        Debug.Assert(int.IsPow2(pageSize));
        return (int)(address & ((uint)pageSize - 1U));
    }
    
    private static uint GetPageIndex(ulong address, int pageSize, out int offset)
    {
        Debug.Assert(int.IsPow2(pageSize));

        offset = GetPageOffset(address, pageSize);
        return (uint)(address >> BitOperations.TrailingZeroCount(pageSize));
    }

    private abstract class PageManager : Disposable
    {
        public readonly int PageSize;
        protected readonly DirectoryInfo Location;

        protected PageManager(DirectoryInfo location, int pageSize)
        {
            Debug.Assert(int.IsPow2(pageSize));

            Location = location;
            PageSize = pageSize;
        }

        public uint GetPageIndex(ulong address, out int offset)
            => WriteAheadLog.GetPageIndex(address, PageSize, out offset);

        protected static IEnumerable<uint> GetPages(DirectoryInfo location)
        {
            return location.EnumerateFiles()
                .Select(static info => uint.TryParse(info.Name, provider: null, out var pageIndex) ? new uint?(pageIndex) : null)
                .Where(static pageIndex => pageIndex.HasValue)
                .Select(static pageIndex => pageIndex.GetValueOrDefault())
                .ToArray();
        }
        
        protected static int GetPages(DirectoryInfo location, out ReadOnlySpan<uint> pages)
        {
            const int minimumDictionaryCapacity = 11;
            pages = GetPages(location).ToArray();
            return Math.Min(pages.Length, minimumDictionaryCapacity);
        }

        public abstract int DeletePages(uint toPage);

        public abstract MemoryManager<byte> GetOrAddPage(uint pageIndex);

        public MemoryManager<byte> this[uint pageIndex]
            => TryGetPage(pageIndex) ?? throw new ArgumentOutOfRangeException(nameof(pageIndex));

        public abstract MemoryManager<byte>? TryGetPage(uint pageIndex);

        public abstract ValueTask FlushAsync(uint startPage, int startOffset, uint endPage, int endOffset, CancellationToken token);

        public MemoryRange GetRange(ulong offset, long length) => new(this, offset, length);

        [StructLayout(LayoutKind.Auto)]
        public readonly struct MemoryRange(PageManager manager, ulong offset, long length) : IEnumerable<ReadOnlyMemory<byte>>
        {
            public Enumerator GetEnumerator() => new(manager, offset, length);

            private IEnumerator<ReadOnlyMemory<byte>> ToClassicEnumerator()
                => GetEnumerator().ToClassicEnumerator<Enumerator, ReadOnlyMemory<byte>>();

            IEnumerator<ReadOnlyMemory<byte>> IEnumerable<ReadOnlyMemory<byte>>.GetEnumerator()
                => ToClassicEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => ToClassicEnumerator();

            public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
            {
                var enumerator = GetEnumerator();
                var result = !enumerator.MoveNext() || !enumerator.HasNext;
                memory = enumerator.Current;
                return result;
            }

            public ReadOnlySequence<byte> ToReadOnlySequence()
            {
                var enumerator = GetEnumerator();
                return !enumerator.MoveNext() || !enumerator.HasNext
                    ? new(enumerator.Current)
                    : ReadMultiSegment(ref enumerator);

                static ReadOnlySequence<byte> ReadMultiSegment(ref Enumerator enumerator)
                {
                    var buffer = new ReadOnlyMemoryArray();
                    var writer = new BufferWriterSlim<ReadOnlyMemory<byte>>(buffer);
                    writer.Add() = enumerator.Current;

                    while (enumerator.MoveNext())
                    {
                        writer.Add() = enumerator.Current;
                    }

                    return Memory.ToReadOnlySequence(writer.WrittenSpan);
                }
            }

            public static implicit operator ReadOnlySequence<byte>(in MemoryRange range) => range.ToReadOnlySequence();

            [StructLayout(LayoutKind.Auto)]
            public struct Enumerator : IEnumerator<Enumerator, ReadOnlyMemory<byte>>
            {
                private readonly PageManager pages;
                private long length;
                private uint pageIndex;
                private int offset;
                private Memory<byte> current;

                public Enumerator(PageManager manager, ulong address, long length)
                {
                    pages = manager;
                    pageIndex = manager.GetPageIndex(address, out offset);
                    this.length = length;
                }

                [UnscopedRef] public readonly ref readonly Memory<byte> Current => ref current;

                ReadOnlyMemory<byte> IEnumerator<Enumerator, ReadOnlyMemory<byte>>.Current => Current;

                public readonly bool HasNext => length > 0L;

                public bool MoveNext()
                {
                    if (HasNext)
                    {
                        var page = pages[pageIndex++];
                        current = page.Memory.Slice(offset).TrimLength(int.CreateSaturating(length));
                        length -= current.Length;
                        offset = 0;
                        return true;
                    }

                    return false;
                }
            }

            [InlineArray(3)]
            private struct ReadOnlyMemoryArray
            {
                private ReadOnlyMemory<byte> element0;
            }
        }
    }

    private abstract class PageManager<TPage>(DirectoryInfo location, int pageSize, int capacity) : PageManager(location, pageSize)
        where TPage : Page
    {
        protected readonly ConcurrentDictionary<uint, TPage> Pages = new(DictionaryConcurrencyLevel, capacity);

        protected abstract void DeletePage(uint pageIndex, TPage page);

        protected abstract TPage CreatePage(uint pageIndex);

        protected abstract void ReleasePage(TPage page);

        public sealed override int DeletePages(uint toPage) // exclusively
        {
            var count = 0;

            foreach (var pageIndex in GetPages(Location))
            {
                if (pageIndex < toPage && Pages.TryRemove(pageIndex, out var page))
                {
                    DeletePage(pageIndex, page);
                    count++;
                }
            }

            return count;
        }

        public sealed override MemoryManager<byte> GetOrAddPage(uint pageIndex)
        {
            TPage? page;
            while (!Pages.TryGetValue(pageIndex, out page))
            {
                page = CreatePage(pageIndex);

                if (Pages.TryAdd(pageIndex, page))
                    break;

                ReleasePage(page);
            }

            return page;
        }

        public sealed override MemoryManager<byte>? TryGetPage(uint pageIndex)
            => Pages.GetValueOrDefault(pageIndex);
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var page in Pages.Values)
                {
                    page.As<IDisposable>().Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    private class AnonymousPageManager : PageManager<AnonymousPage>
    {
        private const int PageCacheSize = sizeof(ulong) * 8;

        private readonly nuint alignment;
        private readonly nint madvise;
        private IndexPool indices;
        private PageCache cache;

        public AnonymousPageManager(DirectoryInfo location, int pageSize)
            : base(location, pageSize, GetPages(location, out var pages))
        {
            indices = new(PageCacheSize - 1);
            cache = new();

            alignment = GetPageAlignment(pageSize, out madvise);

            // populate pages
            AnonymousPage page;
            if (pages.IsEmpty)
            {
                page = RentPage();
                page.Clear();
                Pages.TryAdd(0U, page);
            }
            else
            {
                foreach (var pageIndex in pages)
                {
                    page = RentPage();
                    page.Populate(location, pageIndex);
                    Pages.TryAdd(pageIndex, page);
                }
            }

            // place at least one page to the cache
            if (indices.TryPeek(out var poolIndex))
            {
                MarkAsHugePageIfSupported(page = new(PageSize, alignment));
                cache[poolIndex] = page;
            }
        }

        private unsafe void MarkAsHugePageIfSupported(AnonymousPage page)
        {
            if (madvise is not 0)
            {
                page.ConvertToHugePage((delegate*unmanaged<nint, nint, int, int>)madvise);
            }
        }

        private static nuint GetPageAlignment(int pageSize, out nint madvise)
        {
            nuint alignment;

            if (OperatingSystem.IsLinux())
            {
                const string hpage_pmd_size = "/sys/kernel/mm/transparent_hugepage/hpage_pmd_size";

                if (File.Exists(hpage_pmd_size))
                {
                    ReadOnlySpan<char> transparentHugePageSize = File.ReadAllText(hpage_pmd_size);
                    if (nuint.TryParse(transparentHugePageSize, out alignment)
                        && IsAligned((uint)pageSize, alignment)
                        && NativeLibrary.TryGetExport(NativeLibrary.GetMainProgramHandle(), "madvise", out madvise))
                        goto exit;
                }
            }

            // fallback - no THP/LP support
            madvise = 0;
            alignment = (uint)Environment.SystemPageSize;

            exit:
            return alignment;

            static bool IsAligned(nuint pageSize, nuint alignment)
                => (pageSize & (alignment - 1U)) is 0;
        }

        protected override AnonymousPage CreatePage(uint pageIndex)
            => RentPage();

        private AnonymousPage RentPage()
        {
            AnonymousPage page;
            if (indices.TryTake(out var poolIndex))
            {
                ref var slot = ref cache[poolIndex];
                if (slot is null)
                {
                    try
                    {
                        MarkAsHugePageIfSupported(slot = new(PageSize, alignment) { PoolIndex = poolIndex });
                    }
                    catch
                    {
                        indices.Return(poolIndex);
                        throw;
                    }
                }

                page = slot;
            }
            else
            {
                // do not enable HugePages for the pages out of the pool
                page = new(PageSize, alignment);
            }

            return page;
        }

        protected override void ReleasePage(AnonymousPage page)
        {
            var poolIndex = page.PoolIndex;
            if (poolIndex < 0)
            {
                page.As<IDisposable>().Dispose();
            }
            else
            {
                // THP splits the page on discard, skip this behavior for HugePages
                if (madvise is 0)
                    page.Discard();
                
                indices.Return(poolIndex);
            }
        }

        protected override void DeletePage(uint pageIndex, AnonymousPage page)
        {
            AnonymousPage.Delete(Location, pageIndex);
            ReleasePage(page);
        }

        private ValueTask FlushAsync(uint pageIndex, Range range, CancellationToken token)
            => Pages.TryGetValue(pageIndex, out var page) ? page.FlushAsync(Location, pageIndex, range, token) : ValueTask.CompletedTask;

        public override ValueTask FlushAsync(uint startPage, int startOffset, uint endPage, int endOffset, CancellationToken token)
            => startPage == endPage
                ? FlushAsync(startPage, startOffset..endOffset, token)
                : FlushMultiplePagesAsync(startPage, startOffset, endPage, endOffset, token);

        private async ValueTask FlushMultiplePagesAsync(uint startPage, int startOffset, uint endPage, int endOffset, CancellationToken token)
        {
            await FlushAsync(startPage, startOffset.., token).ConfigureAwait(false);

            while (++startPage < endPage)
            {
                await FlushAsync(startPage, .., token).ConfigureAwait(false);
            }

            Debug.Assert(startPage == endPage);
            await FlushAsync(endPage, ..endOffset, token).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cache.Clear();
            }

            base.Dispose(disposing);
        }

        [InlineArray(PageCacheSize)]
        private struct PageCache
        {
            private AnonymousPage? page0;

            public void Clear()
            {
                foreach (ref var pageRef in this)
                {
                    if (pageRef is { } page)
                    {
                        page.As<IDisposable>().Dispose();
                        pageRef = null;
                    }
                }
            }
        }
    }
    
    private sealed class MemoryMappedPageManager : PageManager<MemoryMappedPage>
    {
        public MemoryMappedPageManager(DirectoryInfo location, int pageSize)
            : base(location, pageSize, GetPages(location, out var pages))
        {
            // populate pages
            if (pages.IsEmpty)
            {
                Pages.TryAdd(0U, new MemoryMappedPage(location, 0U, pageSize));
            }
            else
            {
                foreach (var pageIndex in pages)
                {
                    Pages.TryAdd(pageIndex, new MemoryMappedPage(location, pageIndex, pageSize));
                }
            }
        }

        private void Flush(uint startPage, uint endPage)
        {
            for (var pageIndex = startPage; pageIndex <= endPage; pageIndex++)
            {
                if (Pages.TryGetValue(pageIndex, out var page))
                    page.Flush();
            }
        }

        public override ValueTask FlushAsync(uint startPage, int startOffset, uint endPage, int endOffset, CancellationToken token)
        {
            var task = ValueTask.CompletedTask;
            try
            {
                Flush(startPage, endPage);
            }
            catch (Exception e)
            {
                task = ValueTask.FromException(e);
            }

            return task;
        }

        protected override void DeletePage(uint pageIndex, MemoryMappedPage page)
            => page.DisposeAndDelete();

        protected override MemoryMappedPage CreatePage(uint pageIndex)
            => new(Location, pageIndex, PageSize);

        protected override void ReleasePage(MemoryMappedPage page)
            => page.As<IDisposable>().Dispose();
    }
}