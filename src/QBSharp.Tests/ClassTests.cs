using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Tests;

public sealed class ClassTests
{
    [Fact]
    public async Task ConstructorPropertiesMethodsSubMeAndIndependentInstances()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("""
            Public Class Counter
                Public Property Value As Integer = 1
                Property Label As String
                Property Items As List(Of Integer) = New List(Of Integer)()
                Public Sub New(value As Integer)
                    Me.Value = value
                End Sub
                Public Sub Add(n As Integer)
                    Value = Value + n
                    Items.Add(n)
                    Return
                End Sub
                Public Function Twice() As Integer
                    Return Me.Value * 2
                End Function
            End Class
            Dim a As Counter = New Counter(10)
            Dim b As Counter = New Counter(20)
            a.Add(3)
            a.Label = "first"
            Dim result = a.Twice()
            """, "class.bas"));
        Assert.Equal(26, session.Variables["result"]);
        Assert.Equal(20, await session.EvaluateAsync(new("b.Value")));
        Assert.Equal(0, await session.EvaluateAsync(new("b.Items.Count")));
        Assert.Equal(1, await session.EvaluateAsync(new("a.Items.Count")));
        Assert.Null(await session.EvaluateAsync(new("b.Label")));
        Assert.Null(await session.EvaluateAsync(new("a.Add(0)")));
        Assert.Equal("first", await session.EvaluateAsync(new("a.Label")));
    }
    [Fact]
    public async Task NamespaceQualifiedTypesReopeningAndScriptTypeChecks()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("""
            Namespace Ops.Models
                Class Person
                    Property Name As String = "Ada"
                    Function Copy() As Person
                        Return New Person()
                    End Function
                End Class
            End Namespace
            Namespace Ops
                Namespace Models
                    Class Other
                    End Class
                End Namespace
            End Namespace
            Dim p As Ops.Models.Person = New Ops.Models.Person()
            Dim copy As Ops.Models.Person = p.Copy()
            Dim empty As Ops.Models.Person
            """));
        Assert.Equal("Ada", await session.EvaluateAsync(new("copy.Name")));
        Assert.Null(session.Variables["empty"]);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("p = New Ops.Models.Other()")));
        Assert.Throws<InvalidOperationException>(() => session.SetVariable("p", "wrong"));
        Assert.Equal("Ops.Models.Person", await session.EvaluateAsync(new("\"\" & p")));
    }
    [Fact]
    public async Task DefaultPropertiesSelfReferencesAndBoundMethods()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("""
            Class Node
                Property NextNode As Node
                Property Number As Integer
                Function GetNumber() As Integer
                    Return Number
                End Function
            End Class
            Dim n As Node = New Node()
            n.NextNode = n
            n.Number = 42
            Dim method = n.GetNumber
            """));
        Assert.Equal(42, await session.EvaluateAsync(new("method()")));
        Assert.Equal(42, await session.EvaluateAsync(new("n.NextNode.Number")));
    }
    [Fact]
    public async Task ClrNewGenericNestedTypesQualifiedNamesAndDeclaredAssignments()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("""
            Dim list As List(Of Integer) = New List(Of Integer)(3)
            list.Add(7)
            Dim map As Dictionary(Of String, List(Of Integer)) = New Dictionary(Of String, List(Of Integer))()
            map.Add("values", list)
            Dim other As System.Collections.Generic.List(Of String) = New System.Collections.Generic.List(Of String)()
            other.Add("a")
            Dim date As Date = New Date(2026, 1, 2)
            Dim second As Date = New Date()
            """));
        Assert.IsType<List<int>>(session.Variables["list"]);
        Assert.IsType<Dictionary<string, List<int>>>(session.Variables["map"]);
        Assert.Equal(7, await session.EvaluateAsync(new("map.get_Item(\"values\").get_Item(0)")));
        Assert.Equal(2026, await session.EvaluateAsync(new("date.Year")));
        Assert.Equal(default(DateTime), session.Variables["second"]);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("list = New List(Of String)()")));
    }
    public sealed class Widget(int n) { public int Number { get; } = n; }
    [Fact]
    public async Task RegisteredClrTypesAreHostOptInAndConstructible()
    {
        var options = new ScriptEngineOptions { ClrTypes = new Dictionary<string, Type> { ["Widget"] = typeof(Widget) } };
        await using var session = new ScriptEngine(options: options).CreateSession();
        Assert.Equal(8, await session.EvaluateAsync(new("New Widget(8).Number")));
        await using var isolated = new ScriptEngine().CreateSession();
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await isolated.EvaluateAsync(new("New Widget(8)")));
    }
    [Fact]
    public async Task TopLevelSubAndReturnThroughNestedBlock()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("Dim x = 0\nPublic Sub Change()\nWhile True\nx = 42\nReturn\nEnd While\nEnd Sub\nChange()"));
        Assert.Equal(42, session.Variables["x"]);
    }
    [Theory]
    [InlineData("Class C\nProperty X\nProperty x\nEnd Class")]
    [InlineData("Class C\nSub New()\nEnd Sub\nSub New(x)\nEnd Sub\nEnd Class")]
    [InlineData("Class C\nPrivate Property X\nEnd Class")]
    [InlineData("Class C\nDim x = 1\nEnd Class")]
    [InlineData("Sub New()\nEnd Sub")]
    [InlineData("Sub F()\nReturn 1\nEnd Sub")]
    [InlineData("Namespace N\nDim x = 1\nEnd Namespace")]
    [InlineData("Function F()\nClass C\nEnd Class\nEnd Function")]
    [InlineData("Dim x As List(Of)")]
    [InlineData("Dim x = New List(Of Integer)")]
    [InlineData("Import \"x\" As If")]
    public void UnsupportedOrMalformedDeclarationsAreParserErrors(string source) => Assert.False(new ScriptParser().ParseScript(new(source)).Success);
    [Theory]
    [InlineData("Class C\nEnd Class\nDim x = New C(1)")]
    [InlineData("Class C\nProperty X As Integer\nEnd Class\nDim c = New C()\nc.X = \"bad\"")]
    [InlineData("Class C\nSub New(n As Integer)\nEnd Sub\nEnd Class\nNew C()")]
    [InlineData("Class C\nEnd Class\nC = 1")]
    [InlineData("Class C\nSub F()\nMe = Nothing\nEnd Sub\nEnd Class\nNew C().F()")]
    [InlineData("Class C\nEnd Class\nNew C().Missing")]
    [InlineData("Dim x = 1\nNamespace x\nEnd Namespace")]
    [InlineData("New List()")]
    [InlineData("New Dictionary(Of String)()")]
    [InlineData("New Integer(Of String)()")]
    [InlineData("Class C\nEnd Class\nNew List(Of C)()")]
    [InlineData("New System.IO.FileInfo(\"a\")")]
    public async Task InvalidRuntimeTypesAndMembersAreDiagnosed(string source)
    {
        await using var session = new ScriptEngine().CreateSession();
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new(source)));
    }
    [Fact]
    public void TypeSyntaxIsStructuredAndNestingBounded()
    {
        var parser = new ScriptParser(); var type = parser.ParseType(new("Dictionary(Of String, List(Of Integer))"));
        Assert.True(type.Success); Assert.Equal(2, type.Root!.Arguments.Length); Assert.Equal("Integer", type.Root.Arguments[1].Arguments[0].Name);
        Assert.Equal("Dictionary(Of String, List(Of Integer))", type.Root.ToString());
        Assert.False(parser.ParseType(new(string.Concat(Enumerable.Repeat("List(Of ", 200)) + "Integer" + new string(')', 200))).Success);
    }
    [Fact]
    public async Task ConstructorFailureHasOriginalSourceAndRecursionIsLimited()
    {
        await using var session = new ScriptEngine(options: new() { MaximumCallDepth = 5 }).CreateSession();
        await session.ExecuteAsync(new("Class C\nProperty Another As C = New C()\nEnd Class", "recursive.bas"));
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("New C()")));
        Assert.Equal("QBS2003", error.Diagnostic.Code); Assert.Equal("recursive.bas", error.Diagnostic.Span.File);
    }
}
