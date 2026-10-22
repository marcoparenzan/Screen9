using System.Collections;
using QBSharp.Core.Syntax;
namespace QBSharp.Core.Runtime;
internal sealed partial class ScriptSession
{
    private async ValueTask ExecuteForEach(ForEachStatement loop, Scope scope)
    {
        var source=await Evaluate(loop.Collection,scope).ConfigureAwait(false);
        if(source is not IEnumerable sequence) throw new InvalidOperationException("For Each requires IEnumerable (Nothing is not iterable).");
        object? type=ResolveDeclared(loop.CounterType,scope);
        Cell? existing=loop.CounterType is null?scope.Find(loop.Counter):null;
        var enumerator=sequence.GetEnumerator();
        activeLoops.Push(LoopKind.For);
        try
        {
            while(true)
            {
                Step(loop.Span);
                if(!enumerator.MoveNext()) break;
                cancellation.ThrowIfCancellationRequested();
                var iteration=new Scope(scope);
                if(existing is not null) Set(existing,enumerator.Current);
                else iteration.Cells.Add(loop.Counter,new Cell(ConvertValue(enumerator.Current,type),type));
                try { await Execute(loop.Body,iteration).ConfigureAwait(false); }
                catch(LoopExited exited) when(exited.Kind==LoopKind.For) { break; }
            }
        }
        finally { activeLoops.Pop(); (enumerator as IDisposable)?.Dispose(); }
    }
    private async ValueTask ExecuteSelect(SelectStatement selection, Scope scope)
    {
        var selector=await Evaluate(selection.Selector,scope).ConfigureAwait(false);
        foreach(var clause in selection.Cases)
        {
            Step(clause.Span); bool match=clause.IsElse;
            foreach(var test in clause.Tests)
            {
                Step(test.Span);
                var value=await Evaluate(test.Value,scope).ConfigureAwait(false);
                match=Values.Boolean(Values.Binary(test.Upper is null?test.Operator:BinaryOperator.GreaterOrEqual,selector,value));
                if(match && test.Upper is not null)
                    match=Values.Boolean(Values.Binary(BinaryOperator.LessOrEqual,selector,await Evaluate(test.Upper,scope).ConfigureAwait(false)));
                if(match) break;
            }
            if(match) { await Execute(clause.Body,scope).ConfigureAwait(false); return; }
        }
    }
}
