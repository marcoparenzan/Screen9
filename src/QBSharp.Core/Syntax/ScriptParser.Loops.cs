using QBSharp.Core.Diagnostics;

namespace QBSharp.Core.Syntax;

public sealed partial class ScriptParser
{
    private sealed partial class Reader
    {
        private Stack<LoopKind> loops=[];
        private Block LoopBody(LoopKind kind,params string[] stops)
        {
            loops.Push(kind);try{return Body(stops);}finally{loops.Pop();}
        }
        private ForStatement For(SourceSpan start)
        {
            var name=Name();string? type=TypeName();Expect("=");var initial=Expression();Expect("To");var limit=Expression();
            var step=Match("Step")?Expression():null;LineEnd();var body=LoopBody(LoopKind.For,"Next");Expect("Next");
            if(Current.Kind==TokenKind.Identifier)
            {
                var closing=Name();
                if(!closing.Text.Equals(name.Text,StringComparison.OrdinalIgnoreCase))
                    throw new ParseFailure(new ScriptDiagnostic("QBS1100","Next counter does not match For counter.",closing.Span));
            }
            var result=new ForStatement(name.Text,type,initial,limit,step,body,Range(start));LineEnd();return result;
        }
        private DoStatement Do(SourceSpan start)
        {
            bool preUntil=Match("Until");Expression? before=null;
            if(preUntil || Match("While"))before=Expression();LineEnd();var body=LoopBody(LoopKind.Do,"Loop");Expect("Loop");
            bool postUntil=Match("Until");Expression? after=null;
            if(postUntil || Match("While"))after=Expression();
            if(before is not null && after is not null)Fail("Do supports a condition at the start or end, not both.");
            var result=new DoStatement(before,preUntil,body,after,postUntil,Range(start));LineEnd();return result;
        }
        private ExitLoopStatement ExitLoop(SourceSpan start)
        {
            LoopKind kind;
            if(Match("For"))kind=LoopKind.For;else if(Match("Do"))kind=LoopKind.Do;else if(Match("While"))kind=LoopKind.While;
            else{Fail("Exit requires For, Do or While.");return null!;}
            if(!loops.Contains(kind))Fail("Exit has no matching loop in the current function.");
            var result=new ExitLoopStatement(kind,Range(start));LineEnd();return result;
        }
    }
}
