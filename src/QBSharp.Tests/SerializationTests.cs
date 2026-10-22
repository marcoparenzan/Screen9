using System.Globalization;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.Serialization;

namespace QBSharp.Tests;

public sealed class SerializationTests
{
    public sealed class Model
    { public string Name { get; set; } = ""; public int Count { get; set; } public DateTime Date { get; set; } public List<int> Values { get; set; } = []; }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClrDynamicScalarAndNestedRoundTrips(bool yaml)
    {
        var model = new Model { Name = "yes: [red]città[/]", Count = 42, Date = new(2026, 1, 2), Values = [1, 2] };
        var json = new JsonCodec(); var y = new YamlCodec(); string text = yaml ? y.Serialize(model) : json.Serialize(model);
        var result = Assert.IsType<Model>(yaml ? y.DeserializeClr(text, typeof(Model)) : json.DeserializeClr(text, typeof(Model)));
        Assert.Equal(model.Name, result.Name); Assert.Equal(42, result.Count); Assert.Equal(model.Date, result.Date); Assert.Equal(model.Values, result.Values);
        var data = Assert.IsType<DynamicRecord>(yaml ? y.Deserialize(text) : json.Deserialize(text)); Assert.Equal(42, data.GetValue("COUNT"));
        Assert.Equal(2, Assert.IsType<List<object?>>(data.GetValue("Values")).Count);
        Assert.Null(yaml ? y.Deserialize("null") : json.Deserialize("null"));
        Assert.Equal("yes", yaml ? y.Deserialize(y.Serialize("yes")) : json.Deserialize(json.Serialize("yes")));
    }
    [Theory]
    [InlineData("{\"x\":1,\"X\":2}")]
    [InlineData("{bad}")]
    [InlineData("NaN")]
    public void InvalidJsonAndDuplicateKeysAreRejected(string text) => Assert.ThrowsAny<Exception>(() => new JsonCodec().Deserialize(text));
    [Theory]
    [InlineData("x: &a 1\ny: *a")]
    [InlineData("x: !!str 1")]
    [InlineData("x: !Custom value")]
    [InlineData("x: 1\nX: 2")]
    [InlineData("---\nx: 1\n---\nx: 2")]
    [InlineData("x: .nan")]
    [InlineData("? [a, b]\n: x")]
    public void UnsafeYamlAndInvalidShapesAreRejected(string text) => Assert.ThrowsAny<Exception>(() => new YamlCodec().Deserialize(text));
    [Fact]
    public void YamlSubsetQuotedStringsBooleansNumbersDatesAndEmptyDocument()
    {
        var data = Assert.IsType<DynamicRecord>(new YamlCodec().Deserialize("yesValue: yes\nnumber: 42\nboolean: true\nquoted: '42'\ndate: 2026-01-02\nempty: null\nfloat: 1.25"));
        Assert.Equal("yes", data.GetValue("yesValue")); Assert.Equal(42, data.GetValue("number")); Assert.Equal(true, data.GetValue("boolean"));
        Assert.Equal("42", data.GetValue("quoted")); Assert.Equal("2026-01-02", data.GetValue("date")); Assert.Null(data.GetValue("empty")); Assert.Equal(1.25m, data.GetValue("float"));
        Assert.Null(new YamlCodec().Deserialize(""));
    }
    [Fact]
    public void LimitsCyclesNonFiniteValuesAndCancellationAreEnforced()
    {
        var limits = new SerializationLimits { MaximumDepth = 2, MaximumNodes = 5, MaximumTextLength = 20 };
        Assert.ThrowsAny<Exception>(() => new JsonCodec(limits).Deserialize(new string(' ', 21)));
        Assert.ThrowsAny<Exception>(() => new YamlCodec(limits).Deserialize("a: {b: {c: 1}}"));
        Assert.ThrowsAny<Exception>(() => new JsonCodec(limits).Deserialize("[1,2,3,4,5]"));
        var cyclic = new List<object?>(); cyclic.Add(cyclic);
        Assert.Throws<InvalidOperationException>(() => new JsonCodec().Serialize(cyclic)); Assert.Throws<InvalidOperationException>(() => new YamlCodec().Serialize(cyclic));
        Assert.Throws<InvalidOperationException>(() => new JsonCodec().Serialize(double.NaN));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new JsonCodec().Deserialize("{}", cts.Token)); Assert.ThrowsAny<OperationCanceledException>(() => new YamlCodec().Serialize(42, cts.Token));
    }
    [Fact]
    public void ProjectionHandlesSharedObjectsAndTypedObjectJsonElements()
    {
        var shared = new Model { Name = "a" }; var json = new JsonCodec(); Assert.Contains("a", json.Serialize(new[] { shared, shared }));
        var map = Assert.IsType<Dictionary<string, object>>(json.DeserializeClr("{\"nested\":{\"value\":42}}", typeof(Dictionary<string, object>)));
        var roundTrip = Assert.IsType<DynamicRecord>(json.Deserialize(json.Serialize(map)));
        Assert.Equal(42, Assert.IsType<DynamicRecord>(roundTrip.GetValue("nested")).GetValue("value"));
    }
    [Fact]
    public async Task ScriptClassPropertiesMethodsAndTypedMaterialization()
    {
        var host = new StandardLibraryHost(options: new() { LoadConfiguration = false });
        await using var session = new ScriptEngine().CreateSession(await host.CreateBindingsAsync());
        await session.ExecuteAsync(new("""
            Class Address
                Property City As String = "default"
            End Class
            Class Person
                Property Name As String
                Property Age As Integer
                Property Place As Address
                Property Values As List(Of Integer) = New List(Of Integer)()
                Function Description() As String
                    Return Name & ":" & Age & ":" & Place.City
                End Function
            End Class
            Dim personValue As Person = Json.Deserialize("{""Name"":""Ada"",""Age"":42,""Place"":{""City"":""Roma""},""Values"":[1,2]}", "Person")
            Dim text = Json.Serialize(personValue)
            Dim yamlText = Yaml.Serialize(personValue)
            Dim again As Person = Yaml.Deserialize(yamlText, "Person")
            Dim dynamicData = Json.Deserialize(text)
            dynamicData.Name = "Changed"
            """));
        Assert.Equal("Ada:42:Roma", await session.EvaluateAsync(new("again.Description()")));
        Assert.Equal(2, await session.EvaluateAsync(new("again.Values.Count")));
        Assert.Equal("Changed", await session.EvaluateAsync(new("dynamicData.Name")));
        string text = Assert.IsType<string>(session.Variables["text"]); Assert.DoesNotContain("Me", text); Assert.DoesNotContain("Description", text);
        var bag = Assert.IsAssignableFrom<IScriptPropertyBag>(session.Variables["personValue"]); Assert.Equal(4, bag.Properties.Count);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)bag.Properties).Add("x", 1));
    }
    [Theory]
    [InlineData("Json.Deserialize(\"{\"\"Unknown\"\":1}\", \"C\")")]
    [InlineData("Yaml.Deserialize(\"X: wrong\", \"C\")")]
    [InlineData("Json.Serialize(C)")]
    [InlineData("Json.Serialize(Json)")]
    [InlineData("Json.Deserialize(\"{}\", \"Missing\")")]
    public async Task InvalidScriptSerializationHasCallSiteDiagnostic(string expression)
    {
        await using var session = new ScriptEngine().CreateSession(await new StandardLibraryHost(options: new() { LoadConfiguration = false }).CreateBindingsAsync());
        await session.ExecuteAsync(new("Class C\nProperty X As Integer\nEnd Class"));
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new(expression, "data.bas")));
        Assert.Equal("data.bas", error.Diagnostic.Span.File); Assert.Equal("QBS2001", error.Diagnostic.Code);
    }
    [Fact]
    public async Task ParameterlessConstructorRequiredAndScriptCyclesRejected()
    {
        await using var session = new ScriptEngine().CreateSession(await new StandardLibraryHost(options: new() { LoadConfiguration = false }).CreateBindingsAsync());
        await session.ExecuteAsync(new("Class C\nSub New(n)\nEnd Sub\nEnd Class\nClass Node\nProperty Child As Node\nEnd Class\nDim value = New Node()\nvalue.Child = value"));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("Json.Deserialize(\"{}\", \"C\")")));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("Yaml.Serialize(value)")));
    }
    [Fact]
    public void CultureInvariantSerialization()
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new("it-IT"); Assert.Equal("1.25", new JsonCodec().Serialize(1.25m)); Assert.Equal(1.25m, new YamlCodec().Deserialize("1.25")); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
