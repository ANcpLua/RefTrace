using RefTrace.Core;
using RefTrace.Metadata;

namespace RefTrace.Tests;

// tests/RefTrace.LanguageFixture holds C# 15 and .NET 11 constructs compiled by the pinned SDK.
[TestClass]
public sealed class LanguageMetadataTests
{
    private const string Namespace = "RefTrace.LanguageFixture.";

    private static string FixturePath()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..",
            "RefTrace.LanguageFixture", configuration, "RefTrace.LanguageFixture.dll"));
    }

    private static AssemblyReport Read() => AssemblyInspector.Read(FixturePath(), "*");

    private static ApiType Type(AssemblyReport report, string name) => report.Types.Single(t => t.Name == Namespace + name);

    [TestMethod]
    public void Union_DeclarationAndHandWrittenType_ListTheirCases()
    {
        var report = Read();
        foreach (var name in new[] { "Outcome", "HandWrittenOutcome" })
        {
            var union = Type(report, name);
            Assert.AreEqual("struct", union.Kind, name);
            CollectionAssert.AreEqual(new[] { Namespace + "Approved", Namespace + "Declined" }, union.UnionCases!.ToArray(), name);
            // The declared union's Value getter implements IUnion.Value and is final, not virtual.
            Assert.AreEqual("public System.Object Value { get:public; }", union.Members.Single(m => m.Name == "Value").Signature, name);
        }
        Assert.IsNull(Type(report, "Approved").UnionCases);
    }

    [TestMethod]
    public void Closed_RecordsDirectSubtypesAndLeavesOpenChildrenOpen()
    {
        var report = Read();
        var shape = Type(report, "Shape");
        CollectionAssert.AreEqual(new[] { "closed" }, shape.Modifiers.ToArray());
        CollectionAssert.AreEqual(new[] { Namespace + "Circle", Namespace + "Polygon" }, shape.ClosedDerivedTypes!.ToArray());
        var polygon = Type(report, "Polygon");
        Assert.IsEmpty(polygon.Modifiers);
        Assert.IsNull(polygon.ClosedDerivedTypes);
        Assert.AreEqual(Namespace + "Polygon", Type(report, "Square").BaseType);
        CollectionAssert.AreEqual(new[] { "sealed" }, Type(report, "Circle").Modifiers.ToArray());
    }

    [TestMethod]
    public void ExtensionBlock_ShowsDeclaredMembersWithTheirReceiverAndNoScaffolding()
    {
        var report = Read();
        Assert.IsFalse(report.Types.Any(t => t.Name.Contains("<G>$", StringComparison.Ordinal) || t.Name.Contains("<M>$", StringComparison.Ordinal)));
        var extensions = Type(report, "BufferExtensions");
        CollectionAssert.AreEqual(new[] { "static" }, extensions.Modifiers.ToArray());
        var property = extensions.Members.Single(m => m is { Name: "IsEmpty", Kind: "property" });
        Assert.AreEqual(new ApiParameter("buffer", Namespace + "Buffer", "value", false, null), property.Receiver);
        Assert.AreEqual("extension(RefTrace.LanguageFixture.Buffer buffer) public System.Boolean IsEmpty { get:public; }", property.Signature);
        var indexer = extensions.Members.Single(m => m is { Name: "Item", Kind: "property" });
        Assert.AreEqual("index", indexer.Parameters.Single().Name);
        CollectionAssert.AreEqual(new[] { "get:public", "set:public" }, indexer.Accessors.ToArray());
        Assert.IsNotNull(indexer.Receiver);
        // The static implementation methods are what compiled callers bind to; they stay visible.
        var implementation = extensions.Members.Single(m => m.Name == "get_IsEmpty");
        Assert.IsTrue(implementation.IsStatic);
        Assert.IsNull(implementation.Receiver);
        Assert.AreEqual(Namespace + "Buffer", implementation.Parameters.Single().Type);
    }

    [TestMethod]
    public void RuntimeAsync_KeepsTheDeclaredSignatureAndReflectionNamesTheDeclaredMethod()
    {
        var lookups = Type(Read(), "Lookups");
        var runtimeAsync = lookups.Members.Single(m => m.Name == "RuntimeAsync");
        var stateMachine = lookups.Members.Single(m => m.Name == "StateMachine");
        Assert.AreEqual(runtimeAsync.Signature.Replace("RuntimeAsync(", "StateMachine(", StringComparison.Ordinal), stateMachine.Signature);

        var findings = ReflectionInspector.Read(FixturePath()).Findings;
        var direct = findings.Single(f => f.Caller == Namespace + "Lookups.RuntimeAsync");
        Assert.IsNull(direct.DeclaredMethod);
        var lowered = findings.Single(f => f.DeclaredMethod == Namespace + "Lookups.StateMachine");
        Assert.StartsWith(Namespace + "Lookups+<StateMachine>", lowered.Caller);
        Assert.EndsWith(".MoveNext", lowered.Caller);
        Assert.AreEqual("GetType", lowered.TargetMember);
    }

    [TestMethod]
    public void ExtendedLayout_RecordsTheKindAndReportsAReferenceFieldWithoutLoadingTheType()
    {
        var report = Read();
        var valid = Type(report, "NativeHeader");
        Assert.AreEqual("extended CStruct", valid.Layout);
        Assert.IsEmpty(valid.Notes);
        var broken = Type(report, "BrokenHeader");
        Assert.AreEqual("extended CStruct", broken.Layout);
        var note = broken.Notes.Single();
        Assert.Contains("'Name' (System.String)", note);
        Assert.Contains("TypeLoadException", note);
        Assert.AreEqual("sequential", Type(report, "Outcome").Layout);
    }

    [TestMethod]
    public void MemorySafety_MarksRequiresUnsafeMembersUnderTheUpdatedRules()
    {
        var report = Read();
        Assert.AreEqual(2, report.MemorySafetyRulesVersion);
        var memory = Type(report, "Memory");
        var read = memory.Members.Single(m => m.Name == "Read");
        Assert.IsTrue(read.RequiresUnsafe);
        Assert.Contains("public static unsafe System.Byte Read(", read.Signature);
        Assert.IsFalse(memory.Members.Single(m => m.Name == "Copy").RequiresUnsafe);
    }

    [TestMethod]
    public void MemorySafety_WithoutModuleRules_LeavesRequiresUnsafeUnknown()
    {
        var report = AssemblyInspector.Read(typeof(ApiFixture).Assembly.Location, "RefTrace.Tests.ApiFixture");
        Assert.IsNull(report.MemorySafetyRulesVersion);
        Assert.IsTrue(report.Types.Single().Members.All(m => m.RequiresUnsafe is null));
    }
}
