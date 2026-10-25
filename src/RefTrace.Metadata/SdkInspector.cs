namespace RefTrace.Core;

public sealed record SdkReport(string SdkDirectory, string DirectoryVersion, bool NativeCliPayloadPresent,
    IReadOnlyList<string> NativeCliPayloads, string? RoslynAssemblyVersion, string? RoslynInformationalVersion,
    IReadOnlyList<string> Notes);

public static class SdkInspector
{
    public static SdkReport Read(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath)) throw new InspectionException("SDK directory does not exist.");
        if (!File.Exists(Path.Combine(fullPath, "dotnet.dll")) || !Directory.Exists(Path.Combine(fullPath, "Sdks")))
            throw new InspectionException("Expected a .NET SDK version directory containing dotnet.dll and Sdks/.");
        var native = new[] { "libdotnet-aot.dylib", "libdotnet-aot.so", "dotnet-aot.dll" }
            .Select(name => Path.Combine(fullPath, name)).Where(File.Exists).ToArray();
        var compilerPath = Path.Combine(fullPath, "Roslyn", "bincore", "Microsoft.CodeAnalysis.CSharp.dll");
        var compiler = File.Exists(compilerPath) ? AssemblyInspector.Read(compilerPath) : null;
        return new(fullPath, Path.GetFileName(fullPath), native.Length != 0, native,
            compiler?.Identity.Version, compiler?.InformationalVersion,
            ["Presence of a native CLI payload does not prove that a particular invocation used it.",
             "This inspects the supplied SDK directory; it does not establish Rider's selected toolset.",
             "NativeAOT compilation of your own tool requires separate compatibility and publish checks."]);
    }
}
