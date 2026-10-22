namespace QBSharp.Core.Hosting;

/// <summary>Data/stdlib member dispatch without dynamic assembly generation.</summary>
public interface IScriptMemberProvider
{
    object? GetMember(string name);
    void SetMember(string name, object? value);
}
/// <summary>Shallow property snapshot suitable for serializers; no methods or closure state.</summary>
public interface IScriptPropertyBag
{
    IReadOnlyDictionary<string, object?> Properties { get; }
}
/// <summary>Code/container values that must not be serialized as arbitrary CLR records.</summary>
public interface IScriptOpaqueValue { }
