namespace System.Runtime.CompilerServices;

// Roslyn 5.12.0 reads this attribute by name to opt one method out of runtime async;
// .NET 11.0.0 does not ship the type, so the fixture declares it.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RuntimeAsyncMethodGenerationAttribute(bool runtimeAsync) : Attribute
{
    public bool RuntimeAsync { get; } = runtimeAsync;
}
