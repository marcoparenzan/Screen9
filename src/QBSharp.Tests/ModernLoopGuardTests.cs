using QBSharp.Core.Hosting;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Syntax;

namespace QBSharp.Tests;

public sealed class ModernLoopGuardTests
{
    private static SourceSpan Span=>new("custom.bas",new(0,1,1),0);
    private sealed class Parser(Script root):IScriptParser
    {
        public ParseResult<Script> ParseScript(ScriptSource source,CancellationToken token=default)=>new(root,[]);
        public ParseResult<Expression> ParseExpression(ScriptSource source,CancellationToken token=default)=>new ScriptParser().ParseExpression(source,token);
    }
    [Fact]
    public async Task AstGuardVisitsNewLoopChildrenBeforeExecution()
    {
        Expression value=new LiteralExpression(1,Span);
        for(int i=0;i<140;i++)value=new UnaryExpression(UnaryOperator.Plus,value,Span);
        var root=new Script([new ForStatement("i","Integer",value,new LiteralExpression(1,Span),null,new Block([],Span),Span)],Span);
        await using var session=new ScriptEngine(new Parser(root)).CreateSession();
        var e=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("ignored")));
        Assert.Equal("QBS1101",e.Diagnostic.Code);Assert.Empty(session.Variables);
    }
    [Fact]
    public async Task CustomAstCannotExitCallersLoopFromFunction()
    {
        var function=new FunctionDeclaration("F",[],null,new Block([new ExitLoopStatement(LoopKind.For,Span)],Span),Span);
        var call=new ExpressionStatement(new CallExpression(new NameExpression("F",Span),[],Span),Span);
        var loop=new ForStatement("i","Integer",new LiteralExpression(1,Span),new LiteralExpression(1,Span),null,new Block([function,call],Span),Span);
        await using var s=new ScriptEngine(new Parser(new Script([loop],Span))).CreateSession();
        var e=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("ignored")));Assert.Equal("QBS2001",e.Diagnostic.Code);Assert.Contains("matching active loop",e.Message);
    }
}
