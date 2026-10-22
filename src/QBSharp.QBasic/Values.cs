using System.Collections.ObjectModel;
using System.Globalization;
using QBSharp.Core.Hosting;

namespace QBSharp.QBasic;

internal sealed class BFault(int number,string message) : Exception(message) { public int Number { get; }=number; }
internal sealed class BCell(Func<object?,object?> convert,object? initial,bool readOnly=false)
{
    private object? value=initial;
    public bool Shared { get; set; }
    public bool ReadOnly { get; } = readOnly;
    public string? ArrayElementType { get; init; }
    public string Type { get; init; }="ANY";
    public object? Value { get=>value; set{if(ReadOnly)throw new BFault(5,"Cannot assign CONST.");this.value=convert(value);} }
}
public sealed class QBasicRecord : IScriptMemberProvider, IScriptPropertyBag
{
    internal Dictionary<string,BCell> Fields { get; }=new(StringComparer.OrdinalIgnoreCase);
    public string TypeName { get; }
    internal QBasicRecord(string name)=>TypeName=name;
    public IReadOnlyDictionary<string,object?> Properties=>new ReadOnlyDictionary<string,object?>(Fields.ToDictionary(p=>p.Key,p=>p.Value.Value,StringComparer.OrdinalIgnoreCase));
    public object? GetMember(string name)=>Fields.TryGetValue(name,out var cell)?cell.Value:throw new BFault(5,"Unknown TYPE field.");
    public void SetMember(string name,object? value){if(!Fields.TryGetValue(name,out var cell))throw new BFault(5,"Unknown TYPE field.");cell.Value=value;}
    public override string ToString()=>TypeName;
}
public sealed class QBasicArray
{
    private readonly int[] lower,upper;
    private readonly BCell[] elements;
    public int Rank=>lower.Length;
    public int Length=>elements.Length;
    public string ElementType { get; }
    internal QBasicArray(int[] lower,int[] upper,string type,Func<BCell> create)
    {
        if(lower.Length is <1 or >8||lower.Length!=upper.Length)throw new BFault(9,"Array rank must be1..8.");
        long count=1;for(int i=0;i<lower.Length;i++){if(upper[i]<lower[i])throw new BFault(9,"Invalid array bounds.");count=checked(count*((long)upper[i]-lower[i]+1));if(count>1_000_000)throw new BFault(7,"Array element limit1M.");}
        this.lower=(int[])lower.Clone();this.upper=(int[])upper.Clone();ElementType=type;elements=Enumerable.Range(0,(int)count).Select(_=>create()).ToArray();
    }
    public int GetLowerBound(int dimension)=>lower[dimension];
    public int GetUpperBound(int dimension)=>upper[dimension];
    internal BCell Cell(int[] indices)
    {
        if(indices.Length!=Rank)throw new BFault(9,"Array rank mismatch.");int offset=0;
        for(int i=0;i<Rank;i++){if(indices[i]<lower[i]||indices[i]>upper[i])throw new BFault(9,"Subscript out of range.");offset=checked(offset*(upper[i]-lower[i]+1)+indices[i]-lower[i]);}return elements[offset];
    }
    public object? GetValue(params int[] indices)=>Cell(indices).Value;
    public void SetValue(object? value,params int[] indices)=>Cell(indices).Value=value;
    public override string ToString()=>$"Array({Rank}, {Length}, {ElementType})";
}
internal static class BValues
{
    public static bool Numeric(object? value)=>value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or bool;
    public static double Number(object? value)
    {
        double number=value is bool b?b?-1:0:Numeric(value)?Convert.ToDouble(value,CultureInfo.InvariantCulture):throw new BFault(13,"Numeric value required.");
        if(!double.IsFinite(number))throw new BFault(6,"Non-finite numeric value.");return number;
    }
    public static int Integer(object? value)
    {try{return checked((int)Math.Round(Number(value),MidpointRounding.ToEven));}catch(OverflowException){throw new BFault(6,"Integer overflow.");}}
    public static bool Truth(object? value)=>Number(value)!=0;
    public static short Boolean(bool value)=>(short)(value?-1:0);
    public static string Text(object? value)=>Convert.ToString(value,CultureInfo.InvariantCulture)??"";
    public static object? Binary(string op,object? a,object? b)
    {
        if(op=="+"&&a is string sa&&b is string sb)return sa+sb;
        if(op is "=" or "<>" or "<" or ">" or "<=" or ">=")
        {
            int compare=a is string left&&b is string right?string.CompareOrdinal(left,right):Number(a).CompareTo(Number(b));
            return Boolean(op switch{"="=>compare==0,"<>"=>compare!=0,"<"=>compare<0,">"=>compare>0,"<="=>compare<=0,_=>compare>=0});
        }
        if(op is "AND" or "OR" or "XOR" or "\\" or "MOD")
        {
            int x=Integer(a),y=Integer(b);if(op is "\\" or "MOD"&&y==0)throw new BFault(11,"Division by zero.");
            int result=op switch{"AND"=>x&y,"OR"=>x|y,"XOR"=>x^y,"\\"=>x/y,_=>x%y};
            return a is short&&b is short?(object)checked((short)result):result;
        }
        double first=Number(a),second=Number(b);if(op=="/"&&second==0)throw new BFault(11,"Division by zero.");
        double value=op switch{"+"=>first+second,"-"=>first-second,"*"=>first*second,"/"=>first/second,"^"=>Math.Pow(first,second),_=>throw new BFault(5,"Unknown operator.")};
        if(!double.IsFinite(value))throw new BFault(6,"Non-finite arithmetic result.");
        if(op is not ("/" or "^")&&a is short&&b is short)return checked((short)value);
        if(op is not ("/" or "^")&&a is short or int&&b is short or int)return checked((int)value);
        if(a is double||b is double)return value;float single=(float)value;if(!float.IsFinite(single))throw new BFault(6,"Single overflow.");return single;
    }
}
