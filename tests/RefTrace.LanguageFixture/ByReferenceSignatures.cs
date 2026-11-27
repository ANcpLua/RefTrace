namespace RefTrace.LanguageFixture;

public interface IByReference
{
    void Inspect(in int input, ref readonly int location);
}

public abstract class ByReference
{
    public virtual void Inspect(in int input, ref readonly int location) { }
    public void Read(ref readonly int location) { }

    public virtual unsafe void Suppressed(in delegate* unmanaged[Cdecl, SuppressGCTransition]<ref int> callback) { }

    public void Callback(delegate*<ref int> callback) { }
    public void Callback(ref delegate*<int> callback) { }
    public void Callback(ref delegate*<ref int> callback) { }
}

public static class ByReferenceExtensions
{
    extension(ref readonly int receiver)
    {
        public int ReadLocation(ref readonly long location) => receiver;
    }
}
