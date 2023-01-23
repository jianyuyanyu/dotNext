using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;

namespace DotNext.Buffers;

public static partial class BufferHelpers
{
    [RequiresPreviewFeatures]
    private interface IEndianessTransformation<T>
        where T : unmanaged
    {
        public static abstract T ReverseEndianess(T value);

        public static abstract Vector128<T> LoadAsVector128(ReadOnlySpan<T> buffer);

        public static abstract void StoreAsVector128(Span<T> buffer, Vector128<T> items);

        public static abstract Vector256<T> LoadAsVector256(ReadOnlySpan<T> buffer);

        public static abstract void StoreAsVector256(Span<T> buffer, Vector256<T> items);

        Vector128<byte> ReorderMask { get; }
    }

    /// <summary>
    /// Attempts to read typed sequence of elements from the binary sequence.
    /// </summary>
    /// <typeparam name="T">The type of the elements to read.</typeparam>
    /// <param name="reader">The byte sequence reader instance from which the values are to be read.</param>
    /// <param name="buffer">The output buffer.</param>
    /// <returns><see langword="true"/> if <paramref name="reader"/> has enough elements to read; otherwise, <see langword="false"/>.</returns>
    public static unsafe bool TryRead<T>(this ref SequenceReader<byte> reader, scoped Span<T> buffer)
        where T : unmanaged
    {
        for (int count, offset = 0, maxLength = Array.MaxLength / sizeof(T); (uint)offset < (uint)buffer.Length; offset += count)
        {
            count = buffer.Length > maxLength ? maxLength : buffer.Length - offset;

            if (!reader.TryCopyTo(MemoryMarshal.AsBytes(buffer.Slice(offset, count))))
                return false;
        }

        return true;
    }

    [RequiresPreviewFeatures]
    private static unsafe void ReverseEndianess<T, TTransformation>(Span<T> buffer, TTransformation transformation)
        where T : unmanaged
        where TTransformation : struct, IEndianessTransformation<T>
    {
        if (Ssse3.IsSupported)
        {
            if (Avx2.IsSupported)
            {
                for (var reorderMask256 = Vector256.Create(transformation.ReorderMask, transformation.ReorderMask); buffer.Length >= Vector256<T>.Count; buffer = buffer.Slice(Vector256<T>.Count))
                {
                    TTransformation.StoreAsVector256(
                        buffer,
                        Avx2.Shuffle(TTransformation.LoadAsVector256(buffer).AsByte(), reorderMask256).As<byte, T>());
                }
            }

            for (; buffer.Length >= Vector128<T>.Count; buffer = buffer.Slice(Vector128<T>.Count))
            {
                TTransformation.StoreAsVector128(
                        buffer,
                        Avx2.Shuffle(TTransformation.LoadAsVector128(buffer).AsByte(), transformation.ReorderMask).As<byte, T>());
            }
        }

        // software fallback
        foreach (ref var item in buffer)
            item = TTransformation.ReverseEndianess(item);
    }

    [RequiresPreviewFeatures]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReverseEndianess<T, TTransformation>(Span<T> buffer)
        where T : unmanaged
        where TTransformation : struct, IEndianessTransformation<T>
        => ReverseEndianess(buffer, new TTransformation());

#pragma warning disable CA2252  // TODO: Remove in .NET 7

    /// <summary>
    /// Reverse endianess of 16-bit signed integers in-place.
    /// </summary>
    /// <param name="buffer">The buffer to modify.</param>
    public static void ReverseEndianess(this Span<short> buffer)
        => ReverseEndianess<ushort, UInt16Transformation>(MemoryMarshal.Cast<short, ushort>(buffer));

    /// <summary>
    /// Reverse endianess of 16-bit unsigned integers in-place.
    /// </summary>
    /// <param name="buffer">The buffer to modify.</param>
    [CLSCompliant(false)]
    public static void ReverseEndianess(this Span<ushort> buffer)
        => ReverseEndianess<ushort, UInt16Transformation>(buffer);

    /// <summary>
    /// Reverse endianess of 32-bit signed integers in-place.
    /// </summary>
    /// <param name="buffer">The buffer to modify.</param>
    public static void ReverseEndianess(this Span<int> buffer)
        => ReverseEndianess<uint, UInt32Transformation>(MemoryMarshal.Cast<int, uint>(buffer));

    /// <summary>
    /// Reverse endianess of 32-bit unsigned integers in-place.
    /// </summary>
    /// <param name="buffer">The buffer to modify.</param>
    [CLSCompliant(false)]
    public static void ReverseEndianess(this Span<uint> buffer)
        => ReverseEndianess<uint, UInt32Transformation>(buffer);

    /// <summary>
    /// Reverse endianess of 64-bit signed integers in-place.
    /// </summary>
    /// <param name="buffer">The buffer to modify.</param>
    public static void ReverseEndianess(this Span<long> buffer)
        => ReverseEndianess<ulong, UInt64Transformation>(MemoryMarshal.Cast<long, ulong>(buffer));

    /// <summary>
    /// Reverse endianess of 64-bit unsigned integers in-place.
    /// </summary>
    /// <param name="buffer">The buffer to modify.</param>
    [CLSCompliant(false)]
    public static void ReverseEndianess(this Span<ulong> buffer)
        => ReverseEndianess<ulong, UInt64Transformation>(buffer);

#pragma warning restore CA2252
    [RequiresPreviewFeatures]
    [StructLayout(LayoutKind.Auto)]
    private readonly struct UInt16Transformation : IEndianessTransformation<ushort>
    {
        public UInt16Transformation() => ReorderMask = Vector128.Create(
                (byte)1,
                0,
                3,
                2,
                5,
                4,
                7,
                6,
                9,
                8,
                11,
                10,
                13,
                12,
                15,
                14);

        public Vector128<byte> ReorderMask { get; }

        static ushort IEndianessTransformation<ushort>.ReverseEndianess(ushort value) => BinaryPrimitives.ReverseEndianness(value);

        static unsafe Vector128<ushort> IEndianessTransformation<ushort>.LoadAsVector128(ReadOnlySpan<ushort> buffer)
        {
            fixed (ushort* ptr = buffer)
            {
                return Ssse3.LoadVector128(ptr);
            }
        }

        static unsafe void IEndianessTransformation<ushort>.StoreAsVector128(Span<ushort> buffer, Vector128<ushort> items)
        {
            fixed (ushort* ptr = buffer)
            {
                Ssse3.Store(ptr, items);
            }
        }

        static unsafe Vector256<ushort> IEndianessTransformation<ushort>.LoadAsVector256(ReadOnlySpan<ushort> buffer)
        {
            fixed (ushort* ptr = buffer)
            {
                return Avx2.LoadVector256(ptr);
            }
        }

        static unsafe void IEndianessTransformation<ushort>.StoreAsVector256(Span<ushort> buffer, Vector256<ushort> items)
        {
            fixed (ushort* ptr = buffer)
            {
                Avx2.Store(ptr, items);
            }
        }
    }

    [RequiresPreviewFeatures]
    [StructLayout(LayoutKind.Auto)]
    private readonly struct UInt32Transformation : IEndianessTransformation<uint>
    {
        public UInt32Transformation() => ReorderMask = Vector128.Create(
                (byte)3,
                2,
                1,
                0,
                7,
                6,
                5,
                4,
                11,
                10,
                9,
                8,
                15,
                14,
                13,
                12);

        public Vector128<byte> ReorderMask { get; }

        static uint IEndianessTransformation<uint>.ReverseEndianess(uint value) => BinaryPrimitives.ReverseEndianness(value);

        static unsafe Vector128<uint> IEndianessTransformation<uint>.LoadAsVector128(ReadOnlySpan<uint> buffer)
        {
            fixed (uint* ptr = buffer)
            {
                return Ssse3.LoadVector128(ptr);
            }
        }

        static unsafe void IEndianessTransformation<uint>.StoreAsVector128(Span<uint> buffer, Vector128<uint> items)
        {
            fixed (uint* ptr = buffer)
            {
                Ssse3.Store(ptr, items);
            }
        }

        static unsafe Vector256<uint> IEndianessTransformation<uint>.LoadAsVector256(ReadOnlySpan<uint> buffer)
        {
            fixed (uint* ptr = buffer)
            {
                return Avx2.LoadVector256(ptr);
            }
        }

        static unsafe void IEndianessTransformation<uint>.StoreAsVector256(Span<uint> buffer, Vector256<uint> items)
        {
            fixed (uint* ptr = buffer)
            {
                Avx2.Store(ptr, items);
            }
        }
    }

    [RequiresPreviewFeatures]
    [StructLayout(LayoutKind.Auto)]
    private readonly struct UInt64Transformation : IEndianessTransformation<ulong>
    {
        public UInt64Transformation() => ReorderMask = Vector128.Create(
                (byte)7,
                6,
                5,
                4,
                3,
                2,
                1,
                0,
                15,
                14,
                13,
                12,
                11,
                10,
                9,
                8);

        public Vector128<byte> ReorderMask { get; }

        static ulong IEndianessTransformation<ulong>.ReverseEndianess(ulong value) => BinaryPrimitives.ReverseEndianness(value);

        static unsafe Vector128<ulong> IEndianessTransformation<ulong>.LoadAsVector128(ReadOnlySpan<ulong> buffer)
        {
            fixed (ulong* ptr = buffer)
            {
                return Ssse3.LoadVector128(ptr);
            }
        }

        static unsafe void IEndianessTransformation<ulong>.StoreAsVector128(Span<ulong> buffer, Vector128<ulong> items)
        {
            fixed (ulong* ptr = buffer)
            {
                Ssse3.Store(ptr, items);
            }
        }

        static unsafe Vector256<ulong> IEndianessTransformation<ulong>.LoadAsVector256(ReadOnlySpan<ulong> buffer)
        {
            fixed (ulong* ptr = buffer)
            {
                return Avx2.LoadVector256(ptr);
            }
        }

        static unsafe void IEndianessTransformation<ulong>.StoreAsVector256(Span<ulong> buffer, Vector256<ulong> items)
        {
            fixed (ulong* ptr = buffer)
            {
                Avx2.Store(ptr, items);
            }
        }
    }
}