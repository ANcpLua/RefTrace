using System.Diagnostics;
using RefTrace.Core;
using RefTrace.Metadata;

// A controlled dotnet stand-in for the publish inspector's process-boundary tests.
if (args is ["--version"])
{
    Console.WriteLine("11.0.100-fixture");
    return;
}

if (args is ["msbuild", ..])
{
    Console.WriteLine("""{"Properties":{"TargetFramework":"net11.0","PublishAot":"true"}}""");
    return;
}

if (args is ["--wait-child", var pidFile])
{
    await File.WriteAllTextAsync(pidFile + ".tmp", Environment.ProcessId.ToString());
    File.Move(pidFile + ".tmp", pidFile);
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

if (args is ["publish", var project, ..])
{
    await File.WriteAllTextAsync("parent.pid", Environment.ProcessId.ToString());
    Console.WriteLine(new string('O', 131072));
    Console.Error.WriteLine(new string('E', 131072));
    Console.WriteLine($"PROJECT {project}");
    Console.WriteLine($"DIRECTORY {Directory.GetCurrentDirectory()}");
    Console.WriteLine("stdout Grüße 🚀");
    Console.Error.WriteLine("stderr Grüße 🚀");
    if (Path.GetFileName(project) is "wait.csproj" or "drain.csproj")
    {
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
            ["--wait-child", Path.Combine(Directory.GetCurrentDirectory(), "child.pid")])
        {
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Child fixture did not start.");
        if (Path.GetFileName(project) == "wait.csproj") await Task.Delay(Timeout.InfiniteTimeSpan);
    }
    Environment.ExitCode = Path.GetFileName(project) == "failure.csproj" ? 7 : 0;
    return;
}

if (args is ["--inspect", var file])
{
    var report = ReflectionInspector.Read(file);
    Console.WriteLine($"METHODS {report.MethodBodiesScanned} FINDINGS {report.Findings.Count}");
    return;
}

Console.WriteLine($"READY {Environment.ProcessId} {typeof(AssemblyInspector).Assembly.GetName().Name}");
Console.Out.Flush();
Console.ReadLine();
