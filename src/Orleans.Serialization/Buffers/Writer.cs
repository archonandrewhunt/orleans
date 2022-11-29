using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
#if NETCOREAPP3_1_OR_GREATER
using System.Numerics;
#else
using Orleans.Serialization.Utilities;
#endif
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orleans.Serialization.Buffers.Adaptors;
using Orleans.Serialization.Session;

namespace Orleans.Serialization.Buffers
{
    /// <summary>
    /// Helper methods for creating <see cref="Writer{TBufferWriter}"/> instances.
    /// </summary>
    public static class Writer
    {
        /// <summary>
        /// Creates a writer which writes to the specified destination.
        /// </summary>
        /// <typeparam name="TBufferWriter">The buffer writer output type.</typeparam>
        /// <param name="destination">The destination.</param>
        /// <param name="session">The session.</param>
        /// <returns>A new <see cref="Writer{TBufferWriter}"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<TBufferWriter> Create<TBufferWriter>(TBufferWriter destination, SerializerSession session) where TBufferWriter : IBufferWriter<byte> => new(destination, session);

        /// <summary>
        /// Creates a writer which writes to the specified destination.
        /// </summary>
        /// <param name="destination">The destination.</param>
        /// <param name="session">The session.</param>
        /// <returns>A new <see cref="Writer{TBufferWriter}"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<MemoryStreamBufferWriter> Create(MemoryStream destination, SerializerSession session) => new(new MemoryStreamBufferWriter(destination), session);

        /// <summary>
        /// Creates a writer which writes to the specified destination.
        /// </summary>
        /// <param name="destination">The destination.</param>
        /// <param name="session">The session.</param>
        /// <param name="sizeHint">The size hint.</param>
        /// <returns>A new <see cref="Writer{TBufferWriter}"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<PoolingStreamBufferWriter> CreatePooled(Stream destination, SerializerSession session, int sizeHint = 0) => new(new PoolingStreamBufferWriter(destination, sizeHint), session);

        /// <summary>
        /// Creates a writer which writes to the specified destination.
        /// </summary>
        /// <param name="destination">The destination.</param>
        /// <param name="session">The session.</param>
        /// <param name="sizeHint">The size hint.</param>
        /// <returns>A new <see cref="Writer{TBufferWriter}"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<ArrayStreamBufferWriter> Create(Stream destination, SerializerSession session, int sizeHint = 0) => new(new ArrayStreamBufferWriter(destination, sizeHint), session);

        /// <summary>
        /// Creates a writer which writes to the specified destination.
        /// </summary>
        /// <param name="output">The destination.</param>
        /// <param name="session">The session.</param>
        /// <returns>A new <see cref="Writer{TBufferWriter}"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<SpanBufferWriter> Create(byte[] output, SerializerSession session) => Create(output.AsSpan(), session);

        /// <summary>
        /// Creates a writer which writes to the specified destination.
        /// </summary>
        /// <param name="output">The destination.</param>
        /// <param name="session">The session.</param>
        /// <returns>A new <see cref="Writer{TBufferWriter}"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<MemoryBufferWriter> Create(Memory<byte> output, SerializerSession session) => new(new MemoryBufferWriter(output), session);

        /// <summary>
        /// Creates a writer which writes to the specified destination.
        /// </summary>
        /// <param name="output">The destination.</param>
        /// <param name="session">The session.</param>
        /// <returns>A new <see cref="Writer{TBufferWriter}"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<SpanBufferWriter> Create(Span<byte> output, SerializerSession session) => new(new SpanBufferWriter(output), output, session);

        /// <summary>
        /// Creates a writer which writes to a pooled buffer.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns>A new <see cref="Writer{TBufferWriter}"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<PooledArrayBufferWriter> CreatePooled(SerializerSession session) => new(new PooledArrayBufferWriter(), session);
    }

    /// <summary>
    /// Provides functionality for writing to an output stream.
    /// </summary>
    /// <typeparam name="TBufferWriter">The underlying buffer writer type.</typeparam>
    public ref partial struct Writer<TBufferWriter> where TBufferWriter : IBufferWriter<byte>
    {
#pragma warning disable IDE0044 // Add readonly modifier        
        /// <summary>
        /// The output buffer writer.
        /// </summary>
        private TBufferWriter _output;
#pragma warning restore IDE0044 // Add readonly modifier

        /// <summary>
        /// The current write span.
        /// </summary>
        private Span<byte> _currentSpan;

        /// <summary>
        /// The buffer position within the current span.
        /// </summary>
        private int _bufferPos;

        /// <summary>
        /// The previous buffer's size.
        /// </summary>
        private int _previousBuffersSize;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Writer(TBufferWriter output, SerializerSession session)
        {
            Debug.Assert(output is not SpanBufferWriter);
            _output = output;
            Session = session;
            _currentSpan = _output.GetSpan();
            _bufferPos = default;
            _previousBuffersSize = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Writer(TBufferWriter output, Span<byte> span, SerializerSession session)
        {
            Debug.Assert(output is SpanBufferWriter);
            _output = output;
            Session = session;
            _currentSpan = span;
            _bufferPos = default;
            _previousBuffersSize = default;
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (typeof(TBufferWriter).IsValueType)
            {
                if (_output is IDisposable)
                {
                    ((IDisposable)_output).Dispose();
                }
            }
            else
            {
                (_output as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// Gets the serializer session.
        /// </summary>
        /// <value>The serializer session.</value>
        public SerializerSession Session { get; }

        /// <summary>
        /// Gets the output buffer.
        /// </summary>
        /// <value>The output buffer.</value>
        public TBufferWriter Output => _output;

        /// <summary>
        /// Gets the position.
        /// </summary>
        /// <value>The position.</value>
        public int Position => _previousBuffersSize + _bufferPos;

        /// <summary>
        /// Gets the current writable span.
        /// </summary>
        /// <value>The current writable span.</value>
        public Span<byte> WritableSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _currentSpan.Slice(_bufferPos);
        }

        /// <summary>
        /// Advance the write position in the current span.
        /// </summary>
        /// <param name="length">The number of bytes to advance wirte position by.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AdvanceSpan(int length) => _bufferPos += length;

        /// <summary>
        /// Commit the currently written buffers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Commit()
        {
            _output.Advance(_bufferPos);

            if (!typeof(TBufferWriter).IsValueType || typeof(TBufferWriter) != typeof(SpanBufferWriter))
            {
                _previousBuffersSize += _bufferPos;
                _currentSpan = default;
                _bufferPos = default;
            }
        }

        /// <summary>
        /// Ensures that there are at least <paramref name="length"/> contiguous bytes available to be written.
        /// </summary>
        /// <param name="length">The number of contiguous bytes to ensure.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureContiguous(int length)
        {
            // The current buffer is adequate.
            if (_bufferPos + length < _currentSpan.Length)
            {
                return;
            }

            // The current buffer is inadequate, allocate another.
            Allocate(length);
#if DEBUG
            // Throw if the allocation does not satisfy the request.
            if (_currentSpan.Length < length)
            {
                ThrowTooLarge(length);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ThrowTooLarge(int l) => throw new InvalidOperationException($"Requested buffer length {l} cannot be satisfied by the writer.");
#endif
        }

        /// <summary>
        /// Allocates additional buffer space.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AllocateUnspecified()
        {
            // Commit the bytes which have been written.
            _output.Advance(_bufferPos);

            _currentSpan = _output.GetSpan();

            // Update internal state for the new buffer.
            _previousBuffersSize += _bufferPos;
            _bufferPos = 0;
        }

        /// <summary>
        /// Allocates buffer space for the specified number of bytes.
        /// </summary>
        /// <param name="length">The number of bytes to reserve.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Allocate(int length)
        {
            // Commit the bytes which have been written.
            _output.Advance(_bufferPos);

            // Request a new buffer with at least the requested number of available bytes.
            _currentSpan = _output.GetSpan(length);

            // Update internal state for the new buffer.
            _previousBuffersSize += _bufferPos;
            _bufferPos = 0;
        }

        /// <summary>
        /// Writes the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(scoped ReadOnlySpan<byte> value)
        {
            // Fast path, try copying to the current buffer.
            if (value.Length <= _currentSpan.Length - _bufferPos)
            {
                value.CopyTo(WritableSpan);
                _bufferPos += value.Length;
            }
            else
            {
                WriteMultiSegment(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteMultiSegment(scoped ReadOnlySpan<byte> source)
        {
            var input = source;
            while (true)
            {
                // Write as much as possible/necessary into the current segment.
                var writeSize = Math.Min(_currentSpan.Length - _bufferPos, input.Length);
                input[..writeSize].CopyTo(WritableSpan);
                _bufferPos += writeSize;

                input = input[writeSize..];

                if (input.Length == 0)
                {
                    return;
                }

                // The current segment is full but there is more to write.
                AllocateUnspecified();
            }
        }

        /// <summary>
        /// Writes the provided <see cref="byte"/> to the output buffer.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            nuint bufferPos = (uint)_bufferPos;
            if ((uint)bufferPos < (uint)_currentSpan.Length)
            {
                // https://github.com/dotnet/runtime/issues/72004
                Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), bufferPos) = value;
                _bufferPos = (int)(uint)bufferPos + 1;
            }
            else
            {
                WriteByteSlow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteByteSlow(byte value)
        {
            Allocate(1);
            _currentSpan[0] = value;
            _bufferPos = 1;
        }

        /// <summary>
        /// Writes the provided <see cref="int"/> to the output buffer.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int value) => WriteUInt32((uint)value);

        /// <summary>
        /// Writes the provided <see cref="long"/> to the output buffer.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(long value) => WriteUInt64((ulong)value);

        /// <summary>
        /// Writes the provided <see cref="uint"/> to the output buffer.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value)
        {
            nuint pos = (uint)_bufferPos;
            int newPos = (int)(uint)pos + sizeof(uint);
            if ((uint)newPos <= (uint)_currentSpan.Length)
            {
                _bufferPos = newPos;
                if (!BitConverter.IsLittleEndian) value = BinaryPrimitives.ReverseEndianness(value);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos), value);
            }
            else
            {
                WriteUInt32Slow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteUInt32Slow(uint value)
        {
            Allocate(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(_currentSpan, value);
            _bufferPos = sizeof(uint);
        }

        /// <summary>
        /// Writes the provided <see cref="ulong"/> to the output buffer.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt64(ulong value)
        {
            nuint pos = (uint)_bufferPos;
            int newPos = (int)(uint)pos + sizeof(ulong);
            if ((uint)newPos <= (uint)_currentSpan.Length)
            {
                _bufferPos = newPos;
                if (!BitConverter.IsLittleEndian) value = BinaryPrimitives.ReverseEndianness(value);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos), value);
            }
            else
            {
                WriteUInt64Slow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteUInt64Slow(ulong value)
        {
            Allocate(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(_currentSpan, value);
            _bufferPos = sizeof(ulong);
        }

        /// <summary>
        /// Writes the provided <see cref="uint"/> to the output buffer as a variable-width integer.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt32(uint value)
        {
            var neededBytes = (int)((uint)BitOperations.Log2(value) / 7);

            ulong lower = (((ulong)value << 1) + 1) << neededBytes;

            nuint pos = (uint)_bufferPos;
            if ((uint)pos + sizeof(ulong) <= (uint)_currentSpan.Length)
            {
                _bufferPos = (int)(uint)pos + neededBytes + 1;
                if (!BitConverter.IsLittleEndian) lower = BinaryPrimitives.ReverseEndianness(lower);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos), lower);
            }
            else
            {
                WriteVarUInt32Slow(lower);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteVarUInt32Slow(ulong lower)
        {
            Allocate(sizeof(ulong));

            var neededBytes = BitOperations.TrailingZeroCount((uint)lower) + 1;
            BinaryPrimitives.WriteUInt64LittleEndian(_currentSpan, lower);
            _bufferPos = neededBytes;
        }

        /// <summary>
        /// Writes the provided <see cref="ulong"/> to the output buffer as a variable-width integer.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt64(ulong value)
        {
            nuint pos = (uint)_bufferPos;
            // Since this method writes a ulong plus a ushort worth of bytes unconditionally, ensure that there is sufficient space.
            if ((uint)pos + sizeof(ulong) + sizeof(ushort) <= (uint)_currentSpan.Length)
            {
                var neededBytes = (int)((uint)BitOperations.Log2(value) / 7);
                _bufferPos = (int)(uint)pos + neededBytes + 1;

                ulong lower = ((value << 1) + 1) << neededBytes;

                ref var writeHead = ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos);
                if (!BitConverter.IsLittleEndian) lower = BinaryPrimitives.ReverseEndianness(lower);
                Unsafe.WriteUnaligned(ref writeHead, lower);

                // Write the 2 byte overflow unconditionally
                var upper = value >> (63 - neededBytes);
                writeHead = ref Unsafe.Add(ref writeHead, sizeof(ulong));
                if (!BitConverter.IsLittleEndian) upper = BinaryPrimitives.ReverseEndianness((ushort)upper);
                Unsafe.WriteUnaligned(ref writeHead, (ushort)upper);
            }
            else
            {
                WriteVarUInt64Slow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteVarUInt64Slow(ulong value)
        {
            Allocate(sizeof(ulong) + sizeof(ushort));

            var neededBytes = (int)((uint)BitOperations.Log2(value) / 7);
            _bufferPos = neededBytes + 1;

            ulong lower = ((value << 1) + 1) << neededBytes;
            BinaryPrimitives.WriteUInt64LittleEndian(_currentSpan, lower);

            // Write the 2 byte overflow unconditionally
            var upper = value >> (63 - neededBytes);
            BinaryPrimitives.WriteUInt16LittleEndian(_currentSpan.Slice(sizeof(ulong)), (ushort)upper);
        }
    }
}