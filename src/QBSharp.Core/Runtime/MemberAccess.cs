using System.Collections.Concurrent;
using System.Reflection;

namespace QBSharp.Core.Runtime;

internal sealed class MemberAccess
{
    private readonly ConcurrentDictionary<(Type Type, string Name), MemberInfo[]> cache = new();
    private MemberInfo[] Members(object target, string name) => cache.GetOrAdd((target.GetType(), name.ToUpperInvariant()),
        key => key.Type.GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name.Equals(key.Name, StringComparison.OrdinalIgnoreCase)).ToArray());
    private MemberInfo Data(object target, string name)
    {
        var matches = Members(target, name).Where(m => m is PropertyInfo p && p.GetIndexParameters().Length == 0 || m is FieldInfo).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Missing or ambiguous data member '{name}'.");
        return matches[0];
    }
    public object? Get(object? target, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Data(target, name) switch { PropertyInfo p when p.GetMethod?.IsPublic == true => p.GetValue(target), FieldInfo f => f.GetValue(target), _ => throw new InvalidOperationException("Property is not publicly readable.") };
    }
    public void Set(object? target, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        switch (Data(target, name))
        {
            case PropertyInfo p when p.SetMethod?.IsPublic == true: p.SetValue(target, Values.ConvertTo(value, p.PropertyType)); break;
            case FieldInfo f when !f.IsInitOnly: f.SetValue(target, Values.ConvertTo(value, f.FieldType)); break;
            default: throw new InvalidOperationException("Member is read-only.");
        }
    }
    private static (MethodBase Method, object?[] Args) Select(IEnumerable<MethodBase> methods, string name, object?[] args)
    {
        var candidates = new List<(MethodBase Method, object?[] Args, int Score)>();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (method.ContainsGenericParameters || parameters.Length != args.Length || parameters.Any(p => p.ParameterType.IsByRef)) continue;
            try
            {
                int score = 0; var converted = new object?[args.Length];
                for (int i = 0; i < args.Length; i++)
                { var type = parameters[i].ParameterType; score += args[i]?.GetType() == type ? 0 : type.IsInstanceOfType(args[i]) ? 1 : 2; converted[i] = Values.ConvertTo(args[i], type); }
                candidates.Add((method, converted, score));
            }
            catch (Exception e) when (e is InvalidOperationException or OverflowException or InvalidCastException) { }
        }
        if (candidates.Count == 0) throw new InvalidOperationException($"No applicable public overload '{name}'.");
        int best = candidates.Min(c => c.Score); var selected = candidates.Where(c => c.Score == best).ToArray();
        if (selected.Length != 1) throw new InvalidOperationException($"Ambiguous overload '{name}'.");
        return (selected[0].Method, selected[0].Args);
    }
    public object? Call(object? target, string name, object?[] args)
    {
        ArgumentNullException.ThrowIfNull(target); var selected = Select(Members(target, name).OfType<MethodInfo>(), name, args);
        return selected.Method.Invoke(target, selected.Args);
    }
    public object? Construct(Type type, object?[] args)
    {
        if (type.IsAbstract || type.ContainsGenericParameters) throw new InvalidOperationException("Type cannot be constructed.");
        if (type.IsValueType && args.Length == 0) return Activator.CreateInstance(type);
        var selected = Select(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public), type.Name, args);
        return ((ConstructorInfo)selected.Method).Invoke(selected.Args);
    }
}
