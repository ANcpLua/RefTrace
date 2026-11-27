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
    // The metadata tests inspect this union getter without loading the fixture.
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public object? Value { get; }
}

// A union whose cases come from a member provider, and one whose case is passed by reference.
[Union]
public sealed class ProvidedOutcome : ProvidedOutcome.IUnionMembers
{
    private readonly object? _value;

    private ProvidedOutcome(object? value) => _value = value;

    public interface IUnionMembers
    {
        static ProvidedOutcome Create(Approved value) => Create((object)value);
        static ProvidedOutcome Create(Declined value) => new(value);
        static ProvidedOutcome Create(in long value) => new(value);

        private static ProvidedOutcome Create(object value) => new(value);
        static int Create(int value) => value;
        static ProvidedOutcome Create([In] ref decimal value) => new(value);
        static ProvidedOutcome Create(ref readonly char value) => new(value);
        static ProvidedOutcome Create(out double value)
        {
            value = 0;
            return new(value);
        }
        object? Value { get; }
    }

    object? IUnionMembers.Value => _value;
}

// Nested generic factories must return the union with its enclosing and nested type parameters in order.
public static class Outcomes<TOuter>
{
    [Union]
    public sealed class Result<TInner>(object? value) : Result<TInner>.IUnionMembers
    {
        public interface IUnionMembers
        {
            static Result<TInner> Create(TInner value) => new(value);
            static Outcomes<TInner>.Result<TOuter> Create(Declined value) => new(value);
            object? Value { get; }
        }

        object? IUnionMembers.Value => value;
    }
}

[Union]
public readonly struct Measurement
{
    public Measurement(in int count) => Value = count;
    public Measurement(string label) => Value = label;
    // The metadata tests inspect this union getter without loading the fixture.
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
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

// A generic extension block: the compiler's grouping type names its type parameter $T0.
public static class ListExtensions
{
    extension<T>(List<T> list) where T : class
    {
        public T? FirstOrNull => list.Count == 0 ? null : list[0];
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

// A value type with auto layout is rejected inside extended layout, as is a reference nested deeper.
[StructLayout(LayoutKind.Auto)]
public struct AutoPair
{
    public int First;
    public long Second;
}

[ExtendedLayout(ExtendedLayoutKind.CStruct)]
public struct AutoHolder
{
    public safe AutoPair Pair;
}

public struct Named
{
    public string Name;
}

public struct Wrapper
{
    public int Id;
    public Named Inner;
}

[ExtendedLayout(ExtendedLayoutKind.CStruct)]
public struct DeepHolder
{
    public safe Wrapper Item;
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
