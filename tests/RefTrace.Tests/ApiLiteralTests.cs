using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class ApiLiteralTests
{
    private static ApiMember ReadMember(string name) =>
        AssemblyInspector.Read(typeof(LiteralFixture).Assembly.Location, typeof(LiteralFixture).FullName)
            .Types.Single().Members.Single(member => member.Name == name);

    [TestMethod]
    public void Read_EscapesApostropheInOptionalCharacterDefault()
    {
        var method = ReadMember(nameof(LiteralFixture.WithApostrophe));

        Assert.AreEqual(@"'\''", method.Parameters.Single().DefaultValue);
        Assert.AreEqual(@"public static System.Void WithApostrophe(System.Char value = '\'')", method.Signature);
    }

    [TestMethod]
    public void Read_EscapesApostropheInCharacterFieldSignature()
    {
        var field = ReadMember(nameof(LiteralFixture.Apostrophe));

        Assert.AreEqual(@"public static const System.Char Apostrophe = '\''", field.Signature);
    }

    [TestMethod]
    public void Read_PreservesNeighboringCharacterAndStringEscapes()
    {
        var method = ReadMember(nameof(LiteralFixture.WithOtherLiterals));

        CollectionAssert.AreEqual(new[] { "'a'", @"'\\'", "'\\\"'", @"'\r'", @"'\n'", "\"can't\\\"\\\\\\r\\n\"" },
            method.Parameters.Select(parameter => parameter.DefaultValue).ToArray());
    }
}

public static class LiteralFixture
{
    public const char Apostrophe = '\'';

    public static void WithApostrophe(char value = '\'') { }

    public static void WithOtherLiterals(char letter = 'a', char backslash = '\\', char quote = '"',
        char carriageReturn = '\r', char newline = '\n', string text = "can't\"\\\r\n") { }
}
