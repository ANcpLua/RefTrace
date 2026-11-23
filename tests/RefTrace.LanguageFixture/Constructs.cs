using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RefTrace.LanguageFixture;

public sealed class Approved;
public sealed class Declined;

// A union declaration and a hand-written union type with the same cases.
public union Outcome(Approved, Declined);

[Union]
public readonly struct HandWrittenOutcome
{
    public HandWrittenOutcome(Approved value) => Value = value;
    public HandWrittenOutcome(Declined value) => Value = value;
    public object? Value { get; }
}

// A closed class with a direct sealed subtype, a direct open subtype and an indirect subtype.
public closed class Shape;
public sealed class Circle : Shape;
public class Polygon : Shape;
public sealed class Square : Polygon;

public sealed class Buffer(int[] values)
{
    public int Length => values.Length;
    public int Get(int index) => values[index];
    public void Set(int index, int value) => values[index] = value;
}

// An extension block with an extension property and an extension indexer.
public static class BufferExtensions
{
    extension(Buffer buffer)
    {
        public bool IsEmpty => buffer.Length == 0;

        public int this[int index]
        {
            get => buffer.Get(index);
            set => buffer.Set(index, value);
        }
    }
}

// Directory.Build.props turns runtime async on, so only the opted-out method gets a state machine.
public static class Lookups
{
    public static async Task<Type?> RuntimeAsync(string name)
    {
        await Task.Yield();
        return Type.GetType(name);
    }

    [RuntimeAsyncMethodGeneration(false)]
    public static async Task<Type?> StateMachine(string name)
    {
        await Task.Yield();
        return Type.GetType(name);
    }
}

[ExtendedLayout(ExtendedLayoutKind.CStruct)]
public struct NativeHeader
{
    public safe byte Kind;
    public safe int Length;
}

// Compiles, but the runtime rejects it at load: extended layout cannot hold a reference.
[ExtendedLayout(ExtendedLayoutKind.CStruct)]
public struct BrokenHeader
{
    public safe int Length;
    public safe string Name;
}

// Under the updated memory-safety rules an unsafe member requires an unsafe context at each call.
public static class Memory
{
    public static unsafe byte Read(nint address, int offset)
    {
        var bytes = (byte*)address;
        unsafe
        {
            return bytes[offset];
        }
    }

    public static void Copy(byte[] source, nint destination)
    {
        unsafe
        {
            Marshal.Copy(source, 0, destination, source.Length);
        }
    }
}
