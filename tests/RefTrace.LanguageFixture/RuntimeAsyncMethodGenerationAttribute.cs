// Roslyn 5.12.0 looks up this exact namespace and type name to opt a method out of runtime async.
// The fixture supplies the attribute for .NET 11.0.0.
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class RuntimeAsyncMethodGenerationAttribute(bool runtimeAsync) : Attribute
{
    public bool RuntimeAsync { get; } = runtimeAsync;
}
