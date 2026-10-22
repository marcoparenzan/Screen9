using QBSharp.Core.Syntax;

namespace QBSharp.Core.Runtime;

internal sealed partial class ScriptSession
{
    private sealed class LoopExited(LoopKind kind):Exception { public LoopKind Kind { get; }=kind; }
    private Stack<LoopKind> activeLoops=[];
    private static void NumericLoopValue(object? value)
    {
        if(!Values.Numeric(value))throw new InvalidOperationException("For requires numeric start, limit, step and counter.");
        if(value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f))
            throw new InvalidOperationException("For requires finite numbers.");
    }
    private async ValueTask ExecuteFor(ForStatement loop,Scope scope)
    {
        var initial=await Evaluate(loop.Start,scope).ConfigureAwait(false);
        var limit=await Evaluate(loop.Limit,scope).ConfigureAwait(false);
        var step=loop.Step is null?1:await Evaluate(loop.Step,scope).ConfigureAwait(false);
        NumericLoopValue(initial);NumericLoopValue(limit);NumericLoopValue(step);
        if(Values.Boolean(Values.Binary(BinaryOperator.Equal,step,0)))throw new InvalidOperationException("For Step cannot be zero.");
        Scope control=scope;Cell counter;
        if(loop.CounterType is not null)
        {
            var type=ResolveDeclared(loop.CounterType,scope);
            if(!Values.Numeric(DefaultValue(type)))throw new InvalidOperationException("For As requires a numeric type.");
            control=new Scope(scope);counter=new(DefaultValue(type),type);control.Cells.Add(loop.Counter,counter);
        }
        else counter=scope.Find(loop.Counter);
        if(counter.Type is not null && !Values.Numeric(DefaultValue(counter.Type)))throw new InvalidOperationException("For counter has a nonnumeric declared type.");
        var increment=ConvertValue(step,counter.Type);NumericLoopValue(increment);Set(counter,initial);
        bool ascending=Values.Boolean(Values.Binary(BinaryOperator.Greater,increment,0));
        activeLoops.Push(LoopKind.For);
        try
        {
            while(true)
            {
                Step(loop.Span);NumericLoopValue(counter.Value);
                if(!Values.Boolean(Values.Binary(ascending?BinaryOperator.LessOrEqual:BinaryOperator.GreaterOrEqual,counter.Value,limit)))break;
                try{await Execute(loop.Body,new Scope(control)).ConfigureAwait(false);}
                catch(LoopExited exited) when(exited.Kind==LoopKind.For){break;}
                NumericLoopValue(counter.Value);
                var next=ConvertValue(Values.Binary(BinaryOperator.Add,counter.Value,increment),counter.Type);NumericLoopValue(next);
                if(Values.Boolean(Values.Binary(BinaryOperator.Equal,next,counter.Value)))throw new InvalidOperationException("For counter did not advance (numeric precision).");
                Set(counter,next);
            }
        }
        finally{activeLoops.Pop();}
    }
    private async ValueTask<bool> LoopCondition(Expression condition,bool until,Scope scope)
    {
        bool value=Values.Boolean(await Evaluate(condition,scope).ConfigureAwait(false));return until?!value:value;
    }
    private async ValueTask ExecuteDo(DoStatement loop,Scope scope)
    {
        if(loop.Precondition is not null && loop.Postcondition is not null)throw new InvalidOperationException("Do cannot have both precondition and postcondition.");
        activeLoops.Push(LoopKind.Do);
        try
        {
            while(true)
            {
                Step(loop.Span);
                if(loop.Precondition is not null && !await LoopCondition(loop.Precondition,loop.PreUntil,scope).ConfigureAwait(false))break;
                var iteration=new Scope(scope);
                try{await Execute(loop.Body,iteration).ConfigureAwait(false);}
                catch(LoopExited exited) when(exited.Kind==LoopKind.Do){break;}
                if(loop.Postcondition is not null && !await LoopCondition(loop.Postcondition,loop.PostUntil,iteration).ConfigureAwait(false))break;
            }
        }
        finally{activeLoops.Pop();}
    }
    private async ValueTask ExecuteWhile(WhileStatement loop,Scope scope)
    {
        activeLoops.Push(LoopKind.While);
        try
        {
            while(Values.Boolean(await Evaluate(loop.Condition,scope).ConfigureAwait(false)))
            {
                try{await Execute(loop.Body,scope).ConfigureAwait(false);}
                catch(LoopExited exited) when(exited.Kind==LoopKind.While){break;}
            }
        }
        finally{activeLoops.Pop();}
    }
}
