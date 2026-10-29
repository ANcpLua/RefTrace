using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class ApiSignatureTests
{
    private static ApiType Read() => AssemblyInspector.Read(typeof(SignatureFixture<>).Assembly.Location,
        "RefTrace.Tests.SignatureFixture`1").Types.Single();

    [TestMethod]
    public void Read_SignaturesDistinguishOverloadsAndGenericConstraints()
    {
        var type = Read();
        Assert.AreEqual("class", type.Kind);
        CollectionAssert.AreEqual(new[] { "class", "new()" }, type.GenericParameters.Single().Constraints.ToArray());
        var overloads = type.Members.Where(m => m.Name == "Convert").ToArray();
        Assert.HasCount(2, overloads);
        CollectionAssert.AreEquivalent(new[] { "System.Int32", "System.String" }, overloads.Select(m => m.Parameters.Single().Type).ToArray());
        Assert.AreNotEqual(overloads[0].Signature, overloads[1].Signature);
        Assert.IsTrue(overloads.All(m => m.Type == "T"));
        var generic = type.Members.Single(m => m.Name == "Create");
        Assert.AreEqual("U", generic.Type);
        Assert.AreEqual("U", generic.GenericParameters.Single().Name);
        Assert.Contains("System.IDisposable", generic.GenericParameters.Single().Constraints);
        Assert.Contains("where U : System.IDisposable, new()", generic.Signature);
    }

    [TestMethod]
    public void Read_SignaturesPreserveRefKindsOptionalDefaultsAndArrays()
    {
        var method = Read().Members.Single(m => m.Name == "Transform");
        CollectionAssert.AreEqual(new[] { "ref", "out", "in", "value", "value" }, method.Parameters.Select(p => p.Passing).ToArray());
        Assert.AreEqual("System.Int32&", method.Parameters[0].Type);
        Assert.AreEqual("System.String&", method.Parameters[1].Type);
        Assert.IsTrue(method.Parameters[3].Optional);
        Assert.AreEqual("42", method.Parameters[3].DefaultValue);
        Assert.AreEqual("\"line\\nquote\\\"\"", method.Parameters[4].DefaultValue);
        Assert.AreEqual("T[]", method.Type);
        Assert.Contains("ref System.Int32 value", method.Signature);
        Assert.Contains("out System.String text", method.Signature);
        Assert.Contains("in System.Int64 input", method.Signature);
    }

    [TestMethod]
    public void Read_ReportsConstructorsIndexersEventsFieldsAndAccessorVisibility()
    {
        var members = Read().Members;
        Assert.AreEqual("System.String", members.Single(m => m.Kind == "constructor").Parameters.Single().Type);
        var indexer = members.Single(m => m.Name == "Item");
        Assert.AreEqual("System.Int32", indexer.Parameters.Single().Type);
        CollectionAssert.AreEqual(new[] { "get:public", "set:protected" }, indexer.Accessors.ToArray());
        var property = members.Single(m => m.Name == "Name");
        CollectionAssert.AreEqual(new[] { "get:public", "init:public" }, property.Accessors.ToArray());
        Assert.AreEqual("event", members.Single(m => m.Name == "Changed").Kind);
        Assert.AreEqual("System.EventHandler", members.Single(m => m.Name == "Changed").Type);
        Assert.Contains("const System.Int32 Limit = 7", members.Single(m => m.Name == "Limit").Signature);
        Assert.IsFalse(members.Any(m => m.Name is "Hidden" or "get_Item" or "set_Item" or "add_Changed" or "remove_Changed"));
    }
}

public class SignatureFixture<T> where T : class, new()
{
    static SignatureFixture() => throw new InvalidOperationException("Metadata must never execute this type.");
    public SignatureFixture(string name) => Name = name;
    public T Convert(int value) => throw new NotSupportedException();
    public T Convert(string value) => throw new NotSupportedException();
    public U Create<U>() where U : IDisposable, new() => throw new NotSupportedException();
    public T[] Transform(ref int value, out string text, in long input, int count = 42, string label = "line\nquote\"") => throw new NotSupportedException();
    public string this[int index] { get => throw new NotSupportedException(); protected set => throw new NotSupportedException(); }
    public string Name { get; init; }
    public event EventHandler Changed { add => throw new NotSupportedException(); remove => throw new NotSupportedException(); }
    public const int Limit = 7;
    internal void Hidden() => throw new NotSupportedException();
}
