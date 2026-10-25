using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using RefTrace.Metadata;
using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace RefTrace.Tests;

[TestClass]
public sealed class ReflectionInspectorTests
{
    [TestMethod]
    public void Read_FindsCallsWithCallerOffsetCategoryAndAssembly()
    {
        var report = ReflectionInspector.Read(typeof(ReflectionSamples).Assembly.Location);
        var discovery = report.Findings.Single(f => f.Caller.EndsWith($"{nameof(ReflectionSamples)}.{nameof(ReflectionSamples.Discover)}", StringComparison.Ordinal));
        Assert.AreEqual("System.Type", discovery.TargetType);
        Assert.AreEqual("GetType", discovery.TargetMember);
        Assert.AreEqual("reflection-discovery", discovery.Category);
        Assert.AreEqual("System.Runtime", discovery.TargetAssembly);
        Assert.IsGreaterThanOrEqualTo(0, discovery.IlOffset);
        Assert.StartsWith("0x06", discovery.CallerToken);
        Assert.IsTrue(report.Findings.Any(f => f.Caller.EndsWith($"{nameof(ReflectionSamples)}.{nameof(ReflectionSamples.Load)}", StringComparison.Ordinal)
            && f is { TargetType: "System.Runtime.Loader.AssemblyLoadContext", Category: "assembly-loading" }));
    }

    [TestMethod]
    public void Read_HandlesGenericMethodsAndGenericDeclaringTypes()
    {
        var report = ReflectionInspector.Read(typeof(ReflectionSamples).Assembly.Location);
        Assert.IsTrue(report.Findings.Any(f => f.Caller.EndsWith($"{nameof(ReflectionSamples)}.{nameof(ReflectionSamples.Activate)}", StringComparison.Ordinal)
            && f.TargetMember == "CreateInstance"));
        Assert.IsTrue(report.Findings.Any(f => f.Caller.EndsWith($"{nameof(ReflectionSamples)}.{nameof(ReflectionSamples.CompileExpression)}", StringComparison.Ordinal)
            && f.Category == "expression-compilation"));
    }

    [TestMethod]
    public void Read_TypeofAndLiteralNames_AreNotReflectionFindings()
    {
        var report = ReflectionInspector.Read(typeof(ReflectionSamples).Assembly.Location);
        Assert.IsFalse(report.Findings.Any(f => f.Caller.EndsWith($"{nameof(ReflectionSamples)}.{nameof(ReflectionSamples.Harmless)}", StringComparison.Ordinal)));
        Assert.IsFalse(report.Findings.Any(f => f.Caller.EndsWith($"{nameof(ReflectionSamples)}.{nameof(ReflectionSamples.Literal)}", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Read_ConstantOperandContainingCallByte_IsNotAnInstruction()
    {
        var il = new BlobBuilder();
        il.WriteByte((byte)ILOpCode.Ldc_i4);
        il.WriteInt32(0x28); // A call byte inside an integer operand is not an instruction.
        il.WriteByte((byte)ILOpCode.Pop);
        il.WriteByte((byte)ILOpCode.Ret);
        using var image = SafetyPe.Create(il.ToArray());
        var report = ReflectionInspector.Read(image, "constant.dll");
        Assert.AreEqual(1, report.MethodBodiesScanned);
        Assert.IsEmpty(report.Findings);
    }

    [TestMethod]
    [DataRow("netstandard", 1)]
    [DataRow("System.Runtime", 1)]
    [DataRow("Custom.ReflectionLike.Library", 0)]
    public void Read_RecognizesFrameworkFacadesWithoutMatchingArbitraryLibraries(string scope, int expected)
    {
        using var image = SafetyPe.Create([], reflectionScope: scope);
        var report = ReflectionInspector.Read(image, "facade.dll");
        Assert.HasCount(expected, report.Findings);
        if (expected != 0) Assert.AreEqual(scope, report.Findings[0].TargetAssembly);
    }

    [TestMethod]
    public void Read_OperandBoundariesMatchCliOpcodeDefinitions()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opcode || opcode.OperandType == OperandType.InlineMethod) continue;
            var value = unchecked((ushort)opcode.Value);
            if (!Enum.IsDefined((ILOpCode)value)) continue;
            var bytes = new List<byte>();
            if (opcode.Size == 2) bytes.Add(0xfe);
            bytes.Add((byte)value);
            var length = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 12,
                _ => 4
            };
            bytes.AddRange(opcode.OperandType == OperandType.InlineSwitch
                ? [2, 0, 0, 0, 0x28, 0, 0, 0, 0x28, 0, 0, 0]
                : Enumerable.Repeat((byte)0x28, length));
            bytes.Add(0x2a);
            using var image = SafetyPe.Create(bytes.ToArray());
            var report = ReflectionInspector.Read(image, "operand.dll");
            Assert.IsEmpty(report.Findings, $"Operand bytes of {opcode.Name} were interpreted as instructions.");
        }
    }

    [TestMethod]
    [DataRow(new byte[] { 0x28 })]
    [DataRow(new byte[] { 0xfe })]
    [DataRow(new byte[] { 0x45, 0xff, 0xff, 0xff, 0x7f })]
    public void Read_TruncatedIl_IsRejected(byte[] il)
    {
        Assert.ThrowsExactly<BadImageFormatException>(() =>
        {
            using var image = SafetyPe.Create(il);
            ReflectionInspector.Read(image, "bad-il.dll");
        });
    }
}

// These methods are read as IL fixtures; invoking them would invalidate the tests.
public static class ReflectionSamples
{
    static ReflectionSamples() => throw new InvalidOperationException("Metadata inspection must not execute this type.");
    public static Type? Discover(string name) => Type.GetType(name);
    public static T Activate<T>() => Activator.CreateInstance<T>();
    public static Assembly Load(string path) => AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    public static Func<int> CompileExpression() => Expression.Lambda<Func<int>>(Expression.Constant(1)).Compile();
    public static Type Harmless() => typeof(ReflectionSamples);
    public static string Literal() => "System.Reflection.Assembly.Load";
    [RequiresDynamicCode("A fixture requiring runtime code generation.")]
    public static void DynamicContract() { }
    [RequiresUnreferencedCode("A fixture requiring preserved metadata.")]
    public static void TrimmingContract() { }
}
