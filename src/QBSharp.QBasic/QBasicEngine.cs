using System.Collections.ObjectModel;
using System.Reflection;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.QBasic;

public sealed record QBasicOptions
{
    public TextWriter Output { get; init; }=TextWriter.Null;
    public TextReader Input { get; init; }=TextReader.Null;
    public string BaseDirectory { get; init; }=Directory.GetCurrentDirectory();
    public bool AllowFileIO { get; init; }=true;
    public long MaximumSteps { get; init; }=1_000_000;
    public QBasicStepBudgetMode StepBudgetMode { get; init; }=QBasicStepBudgetMode.Total;
    public TimeSpan InputPollingInterval { get; init; }=TimeSpan.Zero;
    /// <summary>Opt-in scaling of pure elapsed-TIMER wait thresholds, not physics or general TIMER values.</summary>
    public double TimerWaitScale { get; init; }=1;
    public int MaximumCallDepth { get; init; }=128;
    public Func<string> ReadKey { get; init; } = () => "";
    public Func<TimeSpan,CancellationToken,ValueTask> Delay { get; init; } = async (duration,token) => await Task.Delay(duration,token);
    public Func<Screen9.PCTone,CancellationToken,ValueTask>? Audio { get; init; }
    public Action<Screen9.Canvas>? PresentText { get; init; }
    /// <summary>INPUT/LINE INPUT as in DOS: prompt and echo on the QBasic screen (presented frames), keys from ReadKey instead of Input.
    /// For hosts that show the whole screen, such as a single window. Default false: prompt on Output, line from Input.</summary>
    public bool ScreenInput { get; init; }
    public Action<Screen9.PCCanvas>? PresentGraphics { get; init; }
    /// <summary>Flush a deferred frame during waits without falsely marking graphics as modified.</summary>
    public Action? RefreshDisplay { get; init; }
    public Func<DateTime> Clock { get; init; }=()=>DateTime.Now;
    /// <summary>Glyph source for text drawn into graphics screens. Auto uses IBM OEM 8x8/8x14/8x16 fonts when found, else the compact 5x7 font.</summary>
    public QBasicRasterFont RasterFont { get; init; }=QBasicRasterFont.Auto;
    /// <summary>Folder holding cga80woa.fon, ega80woa.fon and vgaoem.fon; null searches QBS_FONT_DIR then the Windows Fonts folder.</summary>
    public string? FontDirectory { get; init; }
}
public sealed class QBasicEngine : IScriptEngine
{
    private readonly QBasicOptions options;
    public QBasicEngine(QBasicOptions? options=null)
    {
        this.options=options??new();if(this.options.MaximumSteps<1||this.options.MaximumCallDepth is <1 or >128)throw new ArgumentOutOfRangeException(nameof(options));
        if(!double.IsFinite(this.options.TimerWaitScale)||this.options.TimerWaitScale is <0.0001 or >1000)throw new ArgumentOutOfRangeException(nameof(options));
        if(!Enum.IsDefined(this.options.StepBudgetMode) || this.options.InputPollingInterval<TimeSpan.Zero || this.options.InputPollingInterval>TimeSpan.FromSeconds(1))throw new ArgumentOutOfRangeException(nameof(options));
    }
    public IScriptSession CreateSession(HostBindings? bindings=null)=>new QBasicSession(options,bindings?.Snapshot());
}
internal sealed class BScope(BScope? globals=null)
{
    public Dictionary<string,BCell> Cells { get; }=new(StringComparer.OrdinalIgnoreCase);
    public BScope? Globals { get; }=globals;
    public string? ReturnName; public BCell? ReturnCell;
}
internal sealed record BErrorContext(int Number,string Message,SourceSpan Span);
internal sealed class BResume(string target) : Exception { public string Target { get; }=target; }
internal sealed class BEnd : Exception;
internal sealed class BFor(BCell cell,double limit,double step)
{ public BCell Cell { get; }=cell; public double Limit { get; }=limit; public double Step { get; }=step; }
internal sealed partial class QBasicSession : IScriptSession
{
    private readonly QBasicOptions options;
    private readonly BScope globals=new();
    private readonly Dictionary<string,object?> hosts=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,(BProcedure Procedure,BProgram Program)> procedures=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,List<BVariable>> types=new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] defaults=Enumerable.Repeat("SINGLE",26).ToArray();
    private readonly Dictionary<BProgram,int> dataPositions=[];
    private readonly BFiles files;
    private Random random=new(0);private float lastRandom;
    private int busy,callDepth,expressionDepth,optionBase,errorNumber,errorLine,printColumn;
    private long steps;
    private bool disposed;
    private CancellationToken cancellation;
    private SourceSpan currentSpan=new("<qbasic>",new(0,1,1),0);
    public QBasicSession(QBasicOptions options,IReadOnlyDictionary<string,object?>? bindings)
    {this.options=options;textDisplay.UseIbmFonts=options.RasterFont==QBasicRasterFont.Auto;textDisplay.FontDirectories=[options.FontDirectory,Environment.GetEnvironmentVariable("QBS_FONT_DIR")];files=new(options);if(bindings is not null)foreach(var pair in bindings)hosts[pair.Key]=pair.Value;}
    private void Idle(){ObjectDisposedException.ThrowIf(disposed,this);if(Volatile.Read(ref busy)!=0)throw new InvalidOperationException("QBasic session is executing.");}
    public IReadOnlyDictionary<string,object?> Variables
    {get{Idle();var result=hosts.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);foreach(var p in globals.Cells)result[p.Key]=p.Value.Value;return new ReadOnlyDictionary<string,object?>(result);}}
    public void SetVariable(string name,object? value)
    {
        Idle();ValidateName(name);
        if(globals.Cells.TryGetValue(name,out var cell)){cell.Value=value;cell.Shared=true;}
        else globals.Cells[name]=new(v=>v,value){Type="ANY",Shared=true};
    }
    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);string root=BNames.Base(name);
        if(root.Length==0||name.Length-root.Length>1||!char.IsLetter(root[0])&&root[0]!='_'||root.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'))throw new ArgumentException("Expected a QBasic identifier.");
    }
    public void RegisterFunction(string name,HostFunction function){Idle();ArgumentNullException.ThrowIfNull(function);ValidateName(name);hosts[BNames.Base(name)]=function;}
    private void Enter(CancellationToken token)
    {ObjectDisposedException.ThrowIf(disposed,this);if(Interlocked.CompareExchange(ref busy,1,0)!=0)throw new InvalidOperationException("Concurrent execution rejected.");cancellation=token;steps=0;callDepth=expressionDepth=0;}
    private void Step(SourceSpan span)
    {currentSpan=span;cancellation.ThrowIfCancellationRequested();if(++steps>options.MaximumSteps)throw new ScriptRuntimeException(new("QBS2401","QBasic step limit exceeded.",span));}
    public async ValueTask ExecuteAsync(ScriptSource source,CancellationToken cancellationToken=default)
    {
        Enter(cancellationToken);
        try
        {
            cancellation.ThrowIfCancellationRequested();var program=new BParser(source,cancellation).Parse();
            foreach(var pair in program.Types)types[pair.Key]=pair.Value;foreach(var pair in program.Procedures)procedures[pair.Key]=(pair.Value,program);
            dataPositions[program]=0;try{await Run(program.Main,program,globals,0,false);}catch(BEnd){}
            if(speaker is not null)await speaker.DrainAsync(cancellation); // background music ends before the program does
        }
        catch(OperationCanceledException){throw;}catch(ScriptRuntimeException){throw;}catch(Exception e){throw Runtime(e);}
        finally{speaker?.Stop();speaker=null;Volatile.Write(ref busy,0);}
    }
    public async ValueTask<object?> EvaluateAsync(ScriptSource source,CancellationToken cancellationToken=default)
    {
        Enter(cancellationToken);
        try{return await Eval(new BParser(source,cancellation).ParseExpression(),globals);}
        catch(OperationCanceledException){throw;}catch(ScriptRuntimeException){throw;}catch(Exception e){throw Runtime(e);}
        finally{Volatile.Write(ref busy,0);}
    }
    private ScriptRuntimeException Runtime(Exception error,BErrorContext? origin=null)=>new(new ScriptDiagnostic("QBS2400",$"QBasic error {ErrorNumber(error)}: {ErrorMessage(error)}"+(origin is null?"":$" Handler failed while processing error {origin.Number}: {origin.Message} at {origin.Span}."),currentSpan));
    private static int ErrorNumber(Exception error)=>error switch{BFault b=>b.Number,FileNotFoundException or DirectoryNotFoundException=>53,UnauthorizedAccessException=>70,IOException=>57,OverflowException=>6,DivideByZeroException=>11,_=>5};
    private static string ErrorMessage(Exception error)=>error is BFault b?b.Message:error is IOException or UnauthorizedAccessException?"File I/O failed.":error is OverflowException?"Overflow.":"Invalid operation or host call.";
    private string ResolveType(string name,string? explicitType=null)=>explicitType??BNames.Suffix(name)??(name.Length>0&&char.IsAsciiLetter(name[0])?defaults[char.ToUpperInvariant(name[0])-'A']:"SINGLE");
    private static BCell ArrayCell(string type,QBasicArray? value,bool shared)
    =>new(v=>v is null||v is QBasicArray a&&a.ElementType.Equals(type,StringComparison.OrdinalIgnoreCase)?v:throw new BFault(13,"Array element type mismatch."),value){Type="ANY",ArrayElementType=type,Shared=shared};
    private BCell NewCell(string type,bool shared=false,int depth=0)
    {return new(v=>ConvertValue(v,type,depth),Default(type,depth)){Type=type,Shared=shared};}
    private object? Default(string type,int depth=0)
    {
        if(depth>64)throw new BFault(7,"TYPE nesting exceeded.");
        if(type.StartsWith("STRING*",StringComparison.OrdinalIgnoreCase))return new string(' ',int.Parse(type[7..],System.Globalization.CultureInfo.InvariantCulture));
        switch(type.ToUpperInvariant()){case "INTEGER":return (short)0;case "LONG":return 0;case "SINGLE":return 0f;case "DOUBLE":return 0d;case "STRING":return "";case "ANY":return null;}
        if(!types.TryGetValue(type,out var fields))throw new BFault(13,"Unknown TYPE '"+type+"'.");
        var record=new QBasicRecord(type);foreach(var f in fields)record.Fields.Add(f.Name,NewCell(ResolveType(f.Name,f.Type),depth:depth+1));return record;
    }
    private object? ConvertValue(object? value,string type,int depth=0)
    {
        if(type=="ANY")return value;
        if(type.StartsWith("STRING*",StringComparison.OrdinalIgnoreCase)){if(value is not string text)throw new BFault(13,"String required.");int size=int.Parse(type[7..],System.Globalization.CultureInfo.InvariantCulture);return text.PadRight(size)[..size];}
        switch(type.ToUpperInvariant())
        {
            case "STRING":return value as string??throw new BFault(13,"String required.");
            case "INTEGER":return checked((short)BValues.Integer(value));case "LONG":return BValues.Integer(value);
            case "SINGLE":{float n=(float)BValues.Number(value);if(!float.IsFinite(n))throw new BFault(6,"Single overflow.");return n;}
            case "DOUBLE":{double n=BValues.Number(value);if(!double.IsFinite(n))throw new BFault(6,"Double overflow.");return n;}
        }
        if(value is not QBasicRecord record||!record.TypeName.Equals(type,StringComparison.OrdinalIgnoreCase))throw new BFault(13,"TYPE mismatch.");
        var copy=(QBasicRecord)Default(type,depth)!;foreach(var field in record.Fields)copy.Fields[field.Key].Value=field.Value.Value;return copy;
    }
    private BCell Cell(string name,BScope scope)
    {
        if(scope.ReturnCell is not null&&BNames.Base(name).Equals(scope.ReturnName,StringComparison.OrdinalIgnoreCase))return scope.ReturnCell;
        if(scope.Cells.TryGetValue(name,out var cell))return cell;
        string baseName=BNames.Base(name);if(name!=baseName&&scope.Cells.TryGetValue(baseName,out cell)&&(cell.Type==BNames.Suffix(name)||cell.ArrayElementType==BNames.Suffix(name)))return cell;
        if(scope.Globals is { } global)
        {
            if(global.Cells.TryGetValue(name,out cell)&&cell.Shared)return cell;
            if(name!=baseName&&global.Cells.TryGetValue(baseName,out cell)&&cell.Shared&&(cell.Type==BNames.Suffix(name)||cell.ArrayElementType==BNames.Suffix(name)))return cell;
        }
        cell=NewCell(ResolveType(name));scope.Cells.Add(name,cell);return cell;
    }
    private async ValueTask<BCell> Reference(BExpr expression,BScope scope)
    {
        switch(expression)
        {
            case BName name:
                if (!scope.Cells.ContainsKey(name.Name) && !(scope.Globals?.Cells.ContainsKey(name.Name) ?? false) && !(scope.ReturnName?.Equals(BNames.Base(name.Name),StringComparison.OrdinalIgnoreCase) ?? false) && (BuiltinExists(name.Name) || procedures.ContainsKey(BNames.Base(name.Name)) || hosts.ContainsKey(BNames.Base(name.Name)))) throw new BFault(13,"Expression is not a ByRef variable.");
                return Cell(name.Name,scope);
            case BCall call:
                if(call.Target is not BName arrayName || BuiltinExists(arrayName.Name) || procedures.ContainsKey(BNames.Base(arrayName.Name)) || Cell(arrayName.Name,scope).Value is not QBasicArray array)throw new BFault(13,"Expected array reference.");
                return call.Args.Count==0?await Reference(call.Target,scope):array.Cell((await Args(call.Args,scope)).Select(BValues.Integer).ToArray());
            case BMember member:
                if(await Eval(member.Target,scope) is QBasicRecord record&&record.Fields.TryGetValue(member.Name,out var field))return field;
                throw new BFault(13,"ByRef member requires a QBasic TYPE.");
            default:throw new BFault(13,"Assignment/reference target required.");
        }
    }
    private async ValueTask<List<object?>> Args(List<BExpr> expressions,BScope scope)
    {var result=new List<object?>();foreach(var e in expressions)result.Add(await Eval(e,scope));return result;}
    private async ValueTask<object?> Eval(BExpr expression,BScope scope)
    {
        Step(expression.Span);if(++expressionDepth>128){expressionDepth--;throw new BFault(28,"Expression/call nesting exceeded.");}
        try
        {
            switch(expression)
            {
                case BLiteral literal:return literal.Value;
                case BName name:
                    if(scope.ReturnCell is not null&&BNames.Base(name.Name).Equals(scope.ReturnName,StringComparison.OrdinalIgnoreCase))return scope.ReturnCell.Value;
                    if(scope.Cells.TryGetValue(name.Name,out var local))return local.Value;
                    if(scope.Globals?.Cells.TryGetValue(name.Name,out var shared)==true&&shared.Shared)return shared.Value;
                    if(hosts.TryGetValue(BNames.Base(name.Name),out var host))return host;
                    if(name.Name.Equals("TRUE",StringComparison.OrdinalIgnoreCase))return (short)-1;if(name.Name.Equals("FALSE",StringComparison.OrdinalIgnoreCase))return (short)0;
                    if(procedures.TryGetValue(BNames.Base(name.Name),out var proc)&&proc.Procedure.Parameters.Count==0)return await Invoke(proc,[],scope);
                    if(BuiltinZero(name.Name))return BNames.Base(name.Name).Equals("INKEY",StringComparison.OrdinalIgnoreCase)?await PollKeyAsync():Builtin(name.Name,[]);
                    return Cell(name.Name,scope).Value;
                case BUnary unary:
                    var value=await Eval(unary.Value,scope);return unary.Op switch{"NOT"=>value is short?(object)(short)~BValues.Integer(value):~BValues.Integer(value),"-"=>BValues.Binary("-",(short)0,value),_=>value};
                case BBinary binary:return BValues.Binary(binary.Op,await Eval(binary.Left,scope),await Eval(binary.Right,scope));
                case BMember member:
                    var target=await Eval(member.Target,scope);if(target is IScriptMemberProvider provider)return provider.GetMember(member.Name);
                    if(target is null)throw new BFault(91,"Object required.");return GetMember(target,member.Name);
                case BCall call:
                    if(call.Target is BName fn)
                    {
                        string key=BNames.Base(fn.Name);
                        if(procedures.TryGetValue(key,out var procedure))return await Invoke(procedure,call.Args,scope);
                        if(BuiltinExists(key))
                        {
                            var arguments=await Args(call.Args,scope);
                            if(key.Equals("INKEY",StringComparison.OrdinalIgnoreCase))
                            {if(arguments.Count!=0)throw new BFault(5,"Builtin argument count mismatch.");return await PollKeyAsync();}
                            return Builtin(key,arguments);
                        }
                        if(hosts.TryGetValue(key,out var function)&&function is HostFunction hostFunction)return await hostFunction(new(cancellation),NormalizeHostArgs(await Args(call.Args,scope)));
                    }
                    var callable=await Eval(call.Target,scope);
                    if(callable is QBasicArray array){if(call.Args.Count==0)return array;return array.Cell((await Args(call.Args,scope)).Select(BValues.Integer).ToArray()).Value;}
                    if(callable is HostFunction callback)return await callback(new(cancellation),NormalizeHostArgs(await Args(call.Args,scope)));
                    if(callable is BMethod method)return InvokeMember(method,NormalizeHostArgs(await Args(call.Args,scope)).ToList());
                    throw new BFault(35,"Undefined procedure or array.");
            }
            throw new BFault(5,"Unknown expression.");
        }
        finally{expressionDepth--;}
    }
    private static IReadOnlyList<object?> NormalizeHostArgs(List<object?> args)=>args.Select(value=>value is short s?(object)(int)s:value is float f ? float.IsFinite(f) && f >= int.MinValue && f < int.MaxValue && f == Math.Truncate(f) ? (object)(int)f : (double)f : value).ToList();
    private sealed record BMethod(object Target,MethodInfo[] Methods);
    private readonly Dictionary<(Type,string),MemberInfo[]> members=[];
    private object? GetMember(object target,string name)
    {
        var key=(target.GetType(),name.ToUpperInvariant());if(!members.TryGetValue(key,out var candidates))members[key]=candidates=key.Item1.GetMembers(BindingFlags.Public|BindingFlags.Instance).Where(m=>m.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
        var property=candidates.OfType<PropertyInfo>().FirstOrDefault(p=>p.GetMethod?.IsPublic==true&&p.GetIndexParameters().Length==0);if(property is not null)return property.GetValue(target);
        var field=candidates.OfType<FieldInfo>().FirstOrDefault();if(field is not null)return field.GetValue(target);
        var methods=candidates.OfType<MethodInfo>().Where(m=>!m.ContainsGenericParameters&&m.GetParameters().All(p=>!p.ParameterType.IsByRef)).ToArray();if(methods.Length>0)return new BMethod(target,methods);
        throw new BFault(438,"Unknown public member.");
    }
    private static object? InvokeMember(BMethod member,List<object?> args)
    {
        List<(MethodInfo Method,object?[] Args,int Score)> matches=[];
        foreach(var method in member.Methods.Where(m=>m.GetParameters().Length==args.Count))
        {
            var parameters=method.GetParameters();var converted=new object?[args.Count];int score=0;bool valid=true;
            for(int i=0;i<args.Count;i++)try{if(args[i] is null||parameters[i].ParameterType.IsInstanceOfType(args[i]))converted[i]=args[i];else{converted[i]=Convert.ChangeType(args[i],parameters[i].ParameterType,System.Globalization.CultureInfo.InvariantCulture);score++;}}catch(Exception){valid=false;break;}
            if(valid)matches.Add((method,converted,score));
        }
        if(matches.Count==0)throw new BFault(13,"No matching host method.");int best=matches.Min(m=>m.Score);var choices=matches.Where(m=>m.Score==best).ToArray();if(choices.Length!=1)throw new BFault(13,"Ambiguous host method.");
        return choices[0].Method.Invoke(member.Target,choices[0].Args);
    }
    private async ValueTask<object?> Invoke((BProcedure Procedure,BProgram Program) entry,List<BExpr> args,BScope caller)
    {
        if(++callDepth>options.MaximumCallDepth){callDepth--;throw new BFault(28,"Procedure depth exceeded.");}
        try
        {
            var procedure=entry.Procedure;if(args.Count!=procedure.Parameters.Count)throw new BFault(5,"Procedure argument count mismatch.");var scope=new BScope(globals);
            if(procedure.Function){scope.ReturnName=procedure.Name;scope.ReturnCell=NewCell(ResolveType(procedure.Name,procedure.Type));}
            for(int i=0;i<args.Count;i++)
            {
                var parameter=procedure.Parameters[i];BCell? reference=null;
                if(!parameter.ByValue)try{reference=await Reference(args[i],caller);}catch(BFault){}
                if(reference?.ReadOnly==true)reference=null;
                object? value=reference is null?await Eval(args[i],caller):reference.Value;
                if(parameter.Array)
                {
                    if(parameter.ByValue)throw new BFault(13,"Array parameters must be ByRef.");
                    if(value is not QBasicArray received)throw new BFault(13,"Array argument required.");
                    string expected=ResolveType(parameter.Name,parameter.Type);if(expected!="ANY"&&!received.ElementType.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new BFault(13,"ByRef array element type mismatch.");
                    scope.Cells[parameter.Name]=reference??ArrayCell(received.ElementType,received,false);
                }
                else if(reference is not null) { string expected=ResolveType(parameter.Name,parameter.Type); if(expected!="ANY" && reference.Type!=expected) throw new BFault(13,"ByRef argument type mismatch."); scope.Cells[parameter.Name]=reference; }
                else{var cell=NewCell(ResolveType(parameter.Name,parameter.Type));cell.Value=value;scope.Cells[parameter.Name]=cell;}
            }
            return await Run(procedure.Code,entry.Program,scope,0,false);
        }
        finally{callDepth--;}
    }
    private async ValueTask<object?> Run(BCode code,BProgram program,BScope scope,int start,bool handler,BErrorContext? origin=null)
    {
        var returns=new Stack<int>();var counters=new Dictionary<int,BFor>();var selectors=new Dictionary<int,object?>();
        string onError="";int failed=-1;bool handling=false;int pc=start;
        var timerWaits=new Dictionary<int,long>();
        int Target(string name)=>code.Labels.TryGetValue(name,out var address)?address:throw new BFault(8,"Label not found: "+name);
        while(pc<code.Instructions.Count)
        {
            var node=code.Instructions[pc];Step(node.Span);int next=pc+1;
            try
            {
                switch(node.Op)
                {
                    case "Defaults":
                        string type=node.Name switch{"DEFINT"=>"INTEGER","DEFLNG"=>"LONG","DEFDBL"=>"DOUBLE","DEFSTR"=>"STRING",_=>"SINGLE"};
                        foreach(var range in (List<string>)node.Data!){int a=char.ToUpperInvariant(range[0])-'A',b=char.ToUpperInvariant(range[2])-'A';if(a>b)throw new BFault(5,"Invalid letter range.");for(int i=a;i<=b;i++)defaults[i]=type;}break;
                    case "OptionBase":optionBase=BValues.Integer(await Eval(node.Args[0],scope));if(optionBase is <0 or >1)throw new BFault(5,"OPTION BASE must be0 or1.");break;
                    case "DIM":case "REDIM":
                        foreach(var variable in (List<BVariable>)node.Data!)
                        {
                            var target=variable.Shared?globals:scope;string elementType=ResolveType(variable.Name,variable.Type);
                            target.Cells.TryGetValue(variable.Name,out var previousArray);
                            if(previousArray is null&&BNames.Suffix(variable.Name) is { } suffix&&target.Cells.TryGetValue(BNames.Base(variable.Name),out var alias)&&alias.ArrayElementType==suffix)previousArray=alias;
                            if(node.Op=="REDIM"&&previousArray?.ArrayElementType is { } declaredType)
                            {
                                if(variable.Type is not null&&!variable.Type.Equals(declaredType,StringComparison.OrdinalIgnoreCase)||BNames.Suffix(variable.Name) is { } specified&&!specified.Equals(declaredType,StringComparison.OrdinalIgnoreCase))throw new BFault(13,"REDIM cannot change element type.");
                                elementType=declaredType;
                            }
                            if(node.Op=="DIM"&&target.Cells.ContainsKey(variable.Name))throw new BFault(10,"Duplicate DIM.");
                            if(variable.Bounds is null){target.Cells[variable.Name]=NewCell(elementType,variable.Shared);}
                            else
                            {
                                QBasicArray? array=null;if(variable.Bounds.Count>0)
                                {int[] lower=new int[variable.Bounds.Count],upper=new int[lower.Length];for(int i=0;i<lower.Length;i++){lower[i]=variable.Bounds[i].Lower is { } low?BValues.Integer(await Eval(low,scope)):optionBase;upper[i]=BValues.Integer(await Eval(variable.Bounds[i].Upper,scope));}array=new(lower,upper,elementType,()=>NewCell(elementType));}
                                if(node.Op=="REDIM"&&previousArray?.ArrayElementType is not null)previousArray.Value=array;else target.Cells[variable.Name]=ArrayCell(elementType,array,variable.Shared);
                            }
                        }break;
                    case "Const":
                        var constant=await Eval(node.Args[0],scope);string constantType=BNames.Suffix(node.Name)??(constant switch { short=>"INTEGER",int=>"LONG",float=>"SINGLE",double=>"DOUBLE",string=>"STRING",_=>throw new BFault(13,"Invalid CONST type.") });
                        constant=ConvertValue(constant,constantType);scope.Cells.Add(node.Name,new(v=>v,constant,true){Shared=true,Type=constantType});break;
                    case "Assign":
                        var assigned=await Eval(node.Args[1],scope);
                        if(node.Args[0] is BMember member)
                        {
                            var obj=await Eval(member.Target,scope);if(obj is IScriptMemberProvider provider)provider.SetMember(member.Name,assigned);
                            else{var property=obj?.GetType().GetProperties().FirstOrDefault(p=>p.Name.Equals(member.Name,StringComparison.OrdinalIgnoreCase)&&p.SetMethod?.IsPublic==true);if(property is null)throw new BFault(438,"Member is read-only or missing.");property.SetValue(obj,Convert.ChangeType(assigned,property.PropertyType,System.Globalization.CultureInfo.InvariantCulture));}
                        }
                        else(await Reference(node.Args[0],scope)).Value=assigned;break;
                    case "Expr":await Eval(node.Args[0],scope);break;
                    case "If":
                        if(!await LoopCondition(node,scope)){next=node.Jump;timerWaits.Remove(pc);}
                        else if(node.TimedWait&&options.StepBudgetMode==QBasicStepBudgetMode.BetweenInputPolls)await YieldTimerWait(timerWaits,pc);
                        break;
                    case "Jump":next=node.Jump;break;
                    case "For":
                        var cell=Cell(node.Name,scope);cell.Value=await Eval(node.Args[0],scope);double limit=BValues.Number(await Eval(node.Args[1],scope)),step=BValues.Number(await Eval(node.Args[2],scope));if(step==0)throw new BFault(5,"FOR STEP cannot be0.");
                        counters[pc]=new(cell,limit,step);if(step>0?BValues.Number(cell.Value)>limit:BValues.Number(cell.Value)<limit)next=node.Jump;break;
                    case "Next":
                        if(!counters.TryGetValue(node.Link,out var counter))throw new BFault(1,"NEXT without FOR.");counter.Cell.Value=BValues.Number(counter.Cell.Value)+counter.Step;
                        if(counter.Step>0?BValues.Number(counter.Cell.Value)<=counter.Limit:BValues.Number(counter.Cell.Value)>=counter.Limit)next=node.Jump;break;
                    case "Select":selectors[pc]=await Eval(node.Args[0],scope);break;
                    case "Case":
                        var tests=(List<(string Op,BExpr A,BExpr? B)>)node.Data!;bool match=tests.Count==0;
                        foreach(var test in tests){var a=await Eval(test.A,scope);match|=test.Op=="RANGE"?BValues.Truth(BValues.Binary(">=",selectors[node.Link],a))&&BValues.Truth(BValues.Binary("<=",selectors[node.Link],await Eval(test.B!,scope))):BValues.Truth(BValues.Binary(test.Op,selectors[node.Link],a));}
                        if(!match)next=node.Jump;break;
                    case "GOTO":next=Target(node.Name);break;
                    case "GOSUB":if(returns.Count>=128)throw new BFault(28,"GOSUB depth128.");returns.Push(next);next=Target(node.Name);break;
                    case "RETURN":if(returns.Count==0)throw new BFault(3,"RETURN without GOSUB.");int returnAddress=returns.Pop();next=node.Name.Length==0?returnAddress:Target(node.Name);break;
                    case "ExitProcedure":return scope.ReturnCell?.Value;
                    case "ReturnValue":if(scope.ReturnCell is null)throw new BFault(5,"Return value outside FUNCTION.");scope.ReturnCell.Value=await Eval(node.Args[0],scope);return scope.ReturnCell.Value;
                    case "End":throw new BEnd();
                    case "OnError":onError=node.Name=="0"?"":node.Name;break;
                    case "RESUME":
                        if(handler)throw new BResume(node.Name);
                        if(!handling)throw new BFault(20,"RESUME without error.");next=node.Name=="NEXT"?failed+1:node.Name.Length==0?failed:Target(node.Name);handling=false;origin=null;errorNumber=errorLine=0;break;
                    case "Read":
                        foreach(var item in node.Args){int position=dataPositions.GetValueOrDefault(program);if(position>=program.Data.Count)throw new BFault(4,"Out of DATA.");var destination=await Reference(item,scope);destination.Value=ConvertData(program.Data[position],destination.Type);dataPositions[program]=position+1;}break;
                    case "RESTORE":dataPositions[program]=node.Name.Length==0?0:program.DataLabels.TryGetValue(node.Name,out int restoredPosition)?restoredPosition:throw new BFault(8,"DATA label missing.");break;
                    case "Swap":var first=await Reference(node.Args[0],scope);var second=await Reference(node.Args[1],scope);var old=first.Value;first.Value=second.Value;second.Value=old;break;
                    case "Randomize":random=new Random(node.Args.Count==0?(int)(options.Clock().Ticks&int.MaxValue):BValues.Integer(await Eval(node.Args[0],scope)));break;
                    case "Print":case "WriteFile":await Print(node,scope);break;
                    case "Input":case "LineInput":await Input(node,scope);break;
                    case "Open":files.Open(BValues.Text(await Eval(node.Args[0],scope)),BValues.Integer(await Eval(node.Args[1],scope)),node.Name);break;
                    case "Close":if(node.Args.Count==0)files.CloseAll();else foreach(var file in node.Args)files.Close(BValues.Integer(await Eval(file,scope)));break;
                    case "Kill":files.Delete(BValues.Text(await Eval(node.Args[0],scope)));break;
                    case "Device":await Device(node,scope);break;
                    case "Unsupported":throw new BFault(5,"QBasic runtime feature not implemented: "+node.Name);
                    default:throw new BFault(5,"Unknown AST instruction.");
                }
            }
            catch(Exception error) when(error is not(OperationCanceledException or ScriptRuntimeException or BEnd or BResume))
            {
                if(onError.Length==0||handling)throw Runtime(error,origin);
                origin=new(ErrorNumber(error),ErrorMessage(error),node.Span);
                errorNumber=ErrorNumber(error);errorLine=node.Span.Start.Line;failed=pc;
                if(onError=="NEXT"){next=pc+1;origin=null;errorNumber=errorLine=0;}
                else if(code.Labels.TryGetValue(onError,out int label)){handling=true;next=label;}
                else if(program.Main.Labels.TryGetValue(onError,out int globalLabel))
                {
                    try{await Run(program.Main,program,globals,globalLabel,true,origin);throw new BFault(20,"Error handler ended without RESUME.");}
                    catch(BResume resume){origin=null;next=resume.Target=="NEXT"?pc+1:resume.Target.Length==0?pc:Target(resume.Target);errorNumber=errorLine=0;}
                }
                else throw new BFault(8,"Error-handler label missing.");
            }
            pc=next;
        }
        return scope.ReturnCell?.Value;
    }
    private static object? ConvertData(object? value,string type)=>type.StartsWith("STRING",StringComparison.OrdinalIgnoreCase)?BValues.Text(value):value is string text?BFiles.ParseNumber(text):value;
    public ValueTask DisposeAsync(){if(disposed)return ValueTask.CompletedTask;Idle();files.CloseAll();disposed=true;return ValueTask.CompletedTask;}
}
