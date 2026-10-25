using RefTrace.Core;
using RefTrace.Metadata;

if (args is ["--inspect", var file])
{
    var report = ReflectionInspector.Read(file);
    Console.WriteLine($"METHODS {report.MethodBodiesScanned} FINDINGS {report.Findings.Count}");
    return;
}

Console.WriteLine($"READY {Environment.ProcessId} {typeof(AssemblyInspector).Assembly.GetName().Name}");
Console.Out.Flush();
Console.ReadLine();
