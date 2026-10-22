using System.Collections.Immutable;
namespace QBSharp.Core.Syntax;
public sealed partial class ScriptParser
{
    private sealed partial class Reader
    {
        private IfStatement IfChain(SourceSpan start)
        {
            Enter();
            try
            {
                var condition = Expression(); Expect("Then"); LineEnd();
                var yes = Body("ElseIf"); Block? no = null;
                if (Match("ElseIf"))
                {
                    var branchStart = Previous.Span;
                    var branch = IfChain(branchStart);
                    no = new Block([branch], branch.Span);
                }
                else if (Match("Else")) { LineEnd(); no = Body("ElseIf"); }
                return new IfStatement(condition, yes, no, Range(start));
            }
            finally { depth--; }
        }
        private ForEachStatement ForEach(SourceSpan start)
        {
            var name = Name(); var type = TypeName(); Expect("In"); var collection = Expression(); LineEnd();
            var body = LoopBody(LoopKind.For,"Next"); Expect("Next");
            if(Current.Kind == TokenKind.Identifier)
            { var closing=Name(); if(!closing.Text.Equals(name.Text,StringComparison.OrdinalIgnoreCase)) Fail("Next counter does not match For Each counter."); }
            var result=new ForEachStatement(name.Text,type,collection,body,Range(start)); LineEnd(); return result;
        }
        private SelectStatement Select(SourceSpan start)
        {
            Expect("Case"); var selector=Expression(); LineEnd();
            var clauses=ImmutableArray.CreateBuilder<CaseClause>(); bool hasElse=false;
            while(Match("Case"))
            {
                var clauseStart=Previous.Span;
                if(hasElse) Fail("Case Else must be last.");
                bool isElse=Match("Else"); hasElse=isElse;
                var tests=ImmutableArray.CreateBuilder<CaseTest>();
                if(!isElse) do
                {
                    var testStart=Current.Span;
                    BinaryOperator op=BinaryOperator.Equal;
                    if(Match("Is"))
                    {
                        if(!Operators.TryGetValue(Current.Text,out var comparison) || comparison.Precedence!=4) Fail("Case Is requires a comparison operator.");
                        op=Operators[Take().Text].Operator;
                    }
                    var value=Expression(); Expression? upper=null;
                    if(Match("To")) { if(op!=BinaryOperator.Equal) Fail("Case Is cannot have a range."); upper=Expression(); }
                    tests.Add(new CaseTest(op,value,upper,Range(testStart)));
                } while(Match(","));
                LineEnd(); var body=Body("Case");
                clauses.Add(new CaseClause(tests.ToImmutable(),body,isElse,Range(clauseStart)));
            }
            Expect("End"); Expect("Select"); var result=new SelectStatement(selector,clauses.ToImmutable(),Range(start)); LineEnd(); return result;
        }
    }
}
