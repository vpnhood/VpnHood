using System.Buffers;
using System.Runtime.InteropServices;

namespace VpnHood.Net.Packets;

/// <summary>
/// The memory a packet is made on, and who takes it back when the packet is disposed.
/// </summary>
/// <remarks>
/// <para>
/// Made from a <see cref="Memory{T}"/>, an array or an <see cref="ArraySegment{T}"/>, which convert to it, the memory
/// stays the caller's: the packet reads and writes it, and leaves it when disposed. Made from an
/// <see cref="IMemoryOwner{T}"/>, or from an array rented from an <see cref="ArrayPool{T}"/>
/// (<see cref="FromRentedArray"/>), the memory is given to the packet made on it, which disposes the owner or returns
/// the array once, when the packet is disposed; when making the packet fails, the memory stays the caller's. Give such
/// memory to one packet only: a copy of the struct is the same memory.
/// </para>
/// <para>
/// A rented array needs no object to own it, so code that makes a packet from each read makes no object besides the
/// packet.
/// </para>
/// </remarks>
public readonly struct PacketMemory
{
    // who takes the memory back: the pool its array was rented from, or its owner; neither for memory the caller keeps
    private readonly Owner _owner;

    /// <summary>
    /// Gets the memory.
    /// </summary>
    public Memory<byte> Memory { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PacketMemory"/> struct over memory the caller keeps.
    /// </summary>
    /// <param name="memory">The memory, which the packet made on it leaves to the caller.</param>
    public PacketMemory(Memory<byte> memory)
    {
        Memory = memory;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PacketMemory"/> struct over an owner's memory, which the packet
    /// made on it disposes.
    /// </summary>
    /// <param name="memoryOwner">The owner of the memory.</param>
    /// <exception cref="ArgumentException"><paramref name="memoryOwner"/> is also an <see cref="ArrayPool{T}"/>.</exception>
    public PacketMemory(IMemoryOwner<byte> memoryOwner)
    {
        // the union keeps the object, not which of its cases it came in as: Return would take it for a pool
        if (memoryOwner is ArrayPool<byte>)
            throw new ArgumentException("A memory owner that is also an ArrayPool is not supported.",
                nameof(memoryOwner));

        Memory = memoryOwner.Memory;
        _owner = new Owner(memoryOwner);
    }

    private PacketMemory(Memory<byte> memory, Owner owner)
    {
        Memory = memory;
        _owner = owner;
    }

    /// <summary>
    /// Makes a <see cref="PacketMemory"/> over an array rented from an <see cref="ArrayPool{T}"/>, which the packet made
    /// on it returns to that pool.
    /// </summary>
    /// <param name="rentedArray">The array.</param>
    /// <param name="arrayPool">
    /// The pool the array was rented from, or <see langword="null"/> for <see cref="ArrayPool{T}.Shared"/>.
    /// </param>
    /// <returns>The memory, over the whole array.</returns>
    public static PacketMemory FromRentedArray(byte[] rentedArray, ArrayPool<byte>? arrayPool = null)
    {
        return new PacketMemory(rentedArray, new Owner(arrayPool ?? ArrayPool<byte>.Shared));
    }

    /// <summary>
    /// Converts memory the caller keeps.
    /// </summary>
    /// <param name="memory">The memory.</param>
    public static implicit operator PacketMemory(Memory<byte> memory)
    {
        return new PacketMemory(memory);
    }

    /// <summary>
    /// Converts an array the caller keeps.
    /// </summary>
    /// <param name="array">The array.</param>
    public static implicit operator PacketMemory(byte[] array)
    {
        return new PacketMemory(array);
    }

    /// <summary>
    /// Converts an array segment the caller keeps.
    /// </summary>
    /// <param name="segment">The array segment.</param>
    public static implicit operator PacketMemory(ArraySegment<byte> segment)
    {
        return new PacketMemory(segment);
    }

    // from ArrayPool<byte>.Shared, unless a pool is given
    internal static PacketMemory Rent(int length, MemoryPool<byte>? memoryPool)
    {
        return memoryPool == null
            ? FromRentedArray(ArrayPool<byte>.Shared.Rent(length))
            : new PacketMemory(memoryPool.Rent(length));
    }

    // the packet's first bytes, still taken back as the whole
    internal PacketMemory Slice(int length)
    {
        return new PacketMemory(Memory[..length], _owner);
    }

    internal void Return()
    {
        switch (_owner.Value) {
            case ArrayPool<byte> arrayPool:
                // always an array: FromRentedArray made the memory on it, and a slice keeps it
                if (!MemoryMarshal.TryGetArray<byte>(Memory, out var segment) || segment.Array == null)
                    throw new InvalidOperationException("The rented memory has no array to return.");

                arrayPool.Return(segment.Array);
                break;

            case IMemoryOwner<byte> memoryOwner:
                memoryOwner.Dispose();
                break;
        }
    }

    // a union: one object field, so memory the caller keeps costs no more than its Memory
    private readonly union Owner(ArrayPool<byte>, IMemoryOwner<byte>);
}
