namespace QBSharp.QBasic;

internal sealed partial class QBasicSession
{
    private static bool TimerExpression(BExpr expression)=>expression is BName name&&name.Name.Equals("TIMER",StringComparison.OrdinalIgnoreCase)
        ||expression is BCall { Target: BName call, Args.Count: 0 }&&call.Name.Equals("TIMER",StringComparison.OrdinalIgnoreCase);

    private async ValueTask<bool> LoopCondition(BInstruction instruction,BScope scope)
    {
        BExpr condition=instruction.Args[0];
        if(!instruction.TimedWait||options.TimerWaitScale==1)return BValues.Truth(await Eval(condition,scope));
        bool negate=condition is BUnary { Op: "NOT" };
        if(negate)condition=((BUnary)condition).Value;
        if(condition is not BBinary comparison || comparison.Left is not BBinary { Op: "-" } elapsed || !TimerExpression(elapsed.Left))
            return BValues.Truth(await Eval(instruction.Args[0],scope));
        // Parser guarantees pure expressions and an empty body. Each operand is evaluated once, in order.
        Step(condition.Span);
        object? left=await Eval(comparison.Left,scope),right=await Eval(comparison.Right,scope);
        bool match=BValues.Truth(BValues.Binary(comparison.Op,left,BValues.Number(right)*options.TimerWaitScale));
        return negate?!match:match;
    }
}
