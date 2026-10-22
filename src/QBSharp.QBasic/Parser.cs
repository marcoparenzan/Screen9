using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.QBasic;

internal sealed partial class BParser
{
    private readonly List<BToken> tokens;
    private readonly ScriptSource source;
    private readonly CancellationToken token;
    private int index, depth;
    private readonly BProgram program=new(new());
    private BCode code;
    private readonly Stack<(string Kind,List<BInstruction> Exits)> loops=new();
    private static readonly HashSet<string> StatementWords = new("DO LOOP FOR NEXT IF ELSE ELSEIF THEN END WHILE WEND RETURN RESUME GOTO GOSUB PRINT INPUT DATA READ RESTORE SUB FUNCTION TYPE DECLARE CALL DIM REDIM CONST ON EXIT SELECT CASE COLOR CLS SYSTEM STOP SCREEN WIDTH PLAY SOUND BEEP PALETTE CIRCLE PSET PUT GET PAINT VIEW LOCATE POKE OPEN CLOSE LINE DEF DEFINT DEFLNG DEFSNG DEFDBL DEFSTR RANDOMIZE SWAP OPTION".Split(' '), StringComparer.OrdinalIgnoreCase);
    public BParser(ScriptSource source,CancellationToken token){this.source=source;this.token=token;tokens=BLexer.Scan(source,token);code=program.Main;}
    private BToken Current=>tokens[index];
    private bool At(string text)=>Current.Is(text);
    private BToken Take(){token.ThrowIfCancellationRequested();return tokens[index++];}
    private bool Eat(string text){if(!At(text))return false;Take();return true;}
    private BToken Need(string text)=>Eat(text)?tokens[index-1]:throw BLexer.Error($"Expected '{text}'.",Current.Span);
    private bool EndLine=>At("\n")||At("<eof>");
    private bool EndStatement=>EndLine||At(":")||At("ELSE");
    private void Separators(){while(Eat("\n")||Eat(":")){} }
    private string Name()
    {
        var t=Take();if(t.Literal is not null||!char.IsLetter(t.Text[0])&&t.Text[0]!='_')throw BLexer.Error("Expected identifier.",t.Span);return t.Text;
    }
    private string Label()=>Current.Literal is short or int?Convert.ToString(Take().Literal,System.Globalization.CultureInfo.InvariantCulture)!:Name();
    private BInstruction Emit(string op,SourceSpan span,string name="",List<BExpr>? args=null,object? data=null)
    {var node=new BInstruction(op,span,name,args,data);code.Instructions.Add(node);if(code.Instructions.Count>100000)throw BLexer.Error("Statement limit exceeded.",span);return node;}
    private bool AtBlockStop(string text)
    {
        if(text.StartsWith("END ",StringComparison.Ordinal))return At("END") && index+1<tokens.Count && tokens[index+1].Is(text[4..]);
        return At(text);
    }
    private void Block(params string[] stops)
    {
        if(++depth>64)throw BLexer.Error("Block nesting limit64.",Current.Span);
        try{Separators();while(!At("<eof>")&&!stops.Any(AtBlockStop)){Statement();if(!EndStatement&&!stops.Any(AtBlockStop))throw BLexer.Error("Expected statement separator.",Current.Span);Separators();}}
        finally{depth--;}
    }
    public BProgram Parse(){Block();Need("<eof>");return program;}
    public BExpr ParseExpression(){var e=Expr();while(Eat("\n")){}Need("<eof>");return e;}
    private string? TypeName()
    {
        if(!Eat("AS"))return null;string name=Name().ToUpperInvariant();
        if(name=="STRING"&&Eat("*")){var length=Take();if(length.Literal is not(short or int)||Convert.ToInt32(length.Literal) is <1 or >32767)throw BLexer.Error("Fixed STRING length must be1..32767.",length.Span);name+="*"+length.Literal;}
        return name;
    }
    private BVariable Variable(bool shared)
    {
        string name=Name();List<BBound>? bounds=null;
        if(Eat("("))
        {
            bounds=[];if(!Eat(")")){do{var first=Expr();bounds.Add(Eat("TO")?new(first,Expr()):new(null,first));}while(Eat(","));Need(")");}
        }
        return new(name,TypeName(),bounds,shared);
    }
    private List<BParameter> Parameters()
    {
        List<BParameter> parameters=[];if(!Eat("("))return parameters;if(Eat(")"))return parameters;
        do{bool byval=Eat("BYVAL");if(!byval)Eat("BYREF");string name=Name();bool array=Eat("(");if(array)Need(")");parameters.Add(new(name,TypeName(),array,byval));}while(Eat(","));Need(")");
        if(parameters.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=parameters.Count)throw BLexer.Error("Duplicate parameter.",Current.Span);return parameters;
    }
    private void Procedure(bool declaration)
    {
        bool function=Eat("FUNCTION");if(!function)Need("SUB");var nameToken=Current;string name=Name();var parameters=Parameters();string? type=TypeName()??BNames.Suffix(name);
        string key=BNames.Base(name);if(function)program.DeclaredFunctions.Add(key);
        if(declaration)return;
        if(code!=program.Main)throw BLexer.Error("Nested procedures are unsupported.",nameToken.Span);
        Need("\n");var body=new BCode();var parent=code;code=body;Block(function?"END FUNCTION":"END SUB");Need("END");Need(function?"FUNCTION":"SUB");code=parent;
        if(!program.Procedures.TryAdd(key,new(key,type,function,parameters,body)))throw BLexer.Error("Duplicate procedure.",nameToken.Span);
    }
    private void Statement()
    {
        var start=Current;string word=start.Text.ToUpperInvariant();
        if((index==0||tokens[index-1].Span.Start.Line<start.Span.Start.Line)&&index+1<tokens.Count&&tokens[index+1].Is(":")&&!StatementWords.Contains(start.Text)&&start.Literal is null&&char.IsLetter(start.Text[0]))
        {Take();if(!code.Labels.TryAdd(start.Text,code.Instructions.Count))throw BLexer.Error("Duplicate label.",start.Span);program.DataLabels.TryAdd(start.Text,program.Data.Count);return;}
        if((index==0||tokens[index-1].Span.Start.Line<start.Span.Start.Line)&&start.Literal is short or int)
        {string label=Label();if(!code.Labels.TryAdd(label,code.Instructions.Count))throw BLexer.Error("Duplicate label.",start.Span);program.DataLabels.TryAdd(label,program.Data.Count);return;}
        if(word is "SUB" or "FUNCTION"){Procedure(false);return;}
        if(Eat("DECLARE")){Procedure(true);return;}
        if(Eat("TYPE"))
        {
            string name=Name();Need("\n");List<BVariable> fields=[];Separators();while(!At("END")&&!At("<eof>")){fields.Add(Variable(false));Need("\n");Separators();}Need("END");Need("TYPE");
            if(fields.Any(f=>f.Bounds is not null)||fields.Select(f=>f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=fields.Count||!program.Types.TryAdd(name,fields))throw BLexer.Error("Invalid or duplicate TYPE.",start.Span);return;
        }
        if(word is "DEFINT" or "DEFLNG" or "DEFSNG" or "DEFDBL" or "DEFSTR")
        {Take();List<string> ranges=[];do{string a=Name();string b=Eat("-")?Name():a;if(a.Length!=1||b.Length!=1||!char.IsAsciiLetter(a[0])||!char.IsAsciiLetter(b[0]))throw BLexer.Error("Expected letter range.",start.Span);ranges.Add(a+"-"+b);}while(Eat(","));Emit("Defaults",start.Span,word,data:ranges);return;}
        if(Eat("DEF"))
        {
            if(Eat("SEG")){Emit("Device",start.Span,"DEF SEG",Eat("=")?[Expr()]:[]);return;}
            string name=Name();var parameters=Parameters();Need("=");var value=Expr();var body=new BCode();body.Instructions.Add(new("ReturnValue",start.Span,args:[value]));
            string key=BNames.Base(name);if(!program.Procedures.TryAdd(key,new(key,BNames.Suffix(name),true,parameters,body)))throw BLexer.Error("Duplicate DEF FN.",start.Span);return;
        }
        if(word is "DIM" or "REDIM")
        {Take();bool shared=Eat("SHARED");List<BVariable> vars=[];do{vars.Add(Variable(shared));}while(Eat(","));Emit(word,start.Span,data:vars);return;}
        if(Eat("CONST"))
        {do{string name=Name();Need("=");Emit("Const",start.Span,name,[Expr()]);}while(Eat(","));return;}
        if(Eat("OPTION")){Need("BASE");Emit("OptionBase",start.Span,args:[Expr()]);return;}
        if(Eat("IF")){If(start.Span);return;}
        if(Eat("FOR")){For(start.Span);return;}
        if(Eat("DO")){Loop(start.Span,false);return;}
        if(Eat("WHILE")){Loop(start.Span,true);return;}
        if(Eat("SELECT")){Need("CASE");Select(start.Span);return;}
        if(Eat("EXIT"))
        {
            string kind=Name().ToUpperInvariant();
            if(kind is "SUB" or "FUNCTION"){Emit("ExitProcedure",start.Span);return;}
            var target=loops.FirstOrDefault(l=>l.Kind==kind);if(target.Exits is null)throw BLexer.Error("EXIT without matching loop.",start.Span);target.Exits.Add(Emit("Jump",start.Span));return;
        }
        if(word is "GOTO" or "GOSUB" or "RESTORE" or "RESUME" or "RETURN")
        {Take();string name=EndStatement?"":word=="RESUME"&&Eat("NEXT")?"NEXT":Label();Emit(word,start.Span,name);return;}
        if(Eat("ON")){Need("ERROR");if(Eat("GOTO"))Emit("OnError",start.Span,Label());else{Need("RESUME");Need("NEXT");Emit("OnError",start.Span,"NEXT");}return;}
        if(Eat("DATA"))
        {
            do
            {
                int begin=index;while(!EndStatement&&!At(","))Take();var items=tokens.GetRange(begin,index-begin);
                object? value=items.Count==1&&items[0].Literal is not null?items[0].Literal:items.Count==2&&items[0].Is("-")&&items[1].Literal is not string&&items[1].Literal is not null?-Convert.ToDouble(items[1].Literal):items.Count==0?"":source.Text[items[0].Span.Start.Offset..(items[^1].Span.Start.Offset+items[^1].Span.Length)].Trim();
                program.Data.Add(value);if(program.Data.Count>100000)throw BLexer.Error("DATA limit exceeded.",start.Span);
            }while(Eat(","));return;
        }
        if(Eat("READ")){Emit("Read",start.Span,args:Targets());return;}
        if(Eat("SWAP")){var a=Postfix();Need(",");Emit("Swap",start.Span,args:[a,Postfix()]);return;}
        if(Eat("RANDOMIZE")){Emit("Randomize",start.Span,args:EndStatement?[]:[Expr()]);return;}
        if(word is "PRINT" or "WRITE"){Take();Print(start.Span,word=="WRITE");return;}
        if(Eat("INPUT")){Input(start.Span,false);return;}
        if(Eat("LINE")) {if(Eat("INPUT")){Input(start.Span,true);return;}GraphicsLine(start.Span);return;}
        if(Eat("OPEN")){var file=Expr();Need("FOR");string mode=Name().ToUpperInvariant();Need("AS");Eat("#");Emit("Open",start.Span,mode,[file,Expr()]);return;}
        if(Eat("CLOSE")){List<BExpr> files=[];if(!EndStatement){do{Eat("#");files.Add(Expr());}while(Eat(","));}Emit("Close",start.Span,args:files);return;}
        if(Eat("KILL")){Emit("Kill",start.Span,args:[Expr()]);return;}
        if(word is "END" or "SYSTEM" or "STOP"){Take();Emit("End",start.Span);return;}
        if(word is "SCREEN" or "WIDTH" or "COLOR" or "CLS" or "LOCATE" or "PLAY" or "SOUND" or "BEEP" or "PALETTE" or "CIRCLE" or "PSET" or "PRESET" or "GET" or "PUT" or "PAINT" or "VIEW" or "POKE" or "OUT" or "SLEEP")
        {Take();DeviceStatement(word,start.Span);return;}
        Eat("CALL");var targetExpr=Postfix();
        if(Eat("=")){Emit("Assign",start.Span,args:[targetExpr,Expr()]);return;}
        List<BExpr> arguments=[];if(!EndStatement){do{arguments.Add(Expr());}while(Eat(","));}
        Emit("Expr",start.Span,args:[arguments.Count>0?new BCall(targetExpr,arguments,start.Span):targetExpr is BName?new BCall(targetExpr,[],start.Span):targetExpr]);
    }
    private void Unsupported(string word,SourceSpan span){while(!EndStatement)Take();Emit("Unsupported",span,word);}
    private List<BExpr> Targets(){List<BExpr> args=[];do{args.Add(Postfix());}while(Eat(","));return args;}
    private void Print(SourceSpan span,bool write)
    {
        BExpr? file=null;if(Eat("#")){file=Expr();Need(",");}
        List<(BExpr? Value,string Separator)> parts=[];
        while(!EndStatement)
        {
            BExpr? value=At(",")||At(";")?null:Expr();string separator=Eat(",")?",":Eat(";")?";":"";parts.Add((value,separator));if(separator.Length==0)break;
        }
        Emit(write?"WriteFile":"Print",span,data:(file,parts));
    }
    private void Input(SourceSpan span,bool line)
    {
        BExpr? file=null,prompt=null;bool question=!line;if(Eat("#")){file=Expr();Need(",");}
        else if(Current.Literal is string){prompt=Expr();if(!Eat(";")){Need(",");question=false;}}
        // QBasic: INPUT shows "? " without prompt or after "prompt;", never after "prompt," or for LINE INPUT.
        Emit(line?"LineInput":"Input",span,args:Targets(),data:(file,prompt,question));
    }
    private void If(SourceSpan span)
    {
        var condition=Expr();var test=Emit("If",span,args:[condition]);
        if(Eat("GOTO")){Emit("GOTO",span,Label());test.Jump=code.Instructions.Count;return;}
        Need("THEN");
        if(!Eat("\n"))
        {
            while(!EndLine&&!At("ELSE")){Statement();if(!Eat(":"))break;}
            var jump=Emit("Jump",span);test.Jump=code.Instructions.Count;
            if(Eat("ELSE")){while(!EndLine){Statement();if(!Eat(":"))break;}}
            jump.Jump=code.Instructions.Count;return;
        }
        List<BInstruction> ends=[];
        while(true)
        {
            Block("ELSE","ELSEIF","END IF");ends.Add(Emit("Jump",span));test.Jump=code.Instructions.Count;
            if(Eat("ELSEIF")){var next=Expr();Need("THEN");Need("\n");test=Emit("If",span,args:[next]);continue;}
            if(Eat("ELSE")){Eat("\n");Block("END IF");}
            Need("END");Need("IF");break;
        }
        foreach(var end in ends)end.Jump=code.Instructions.Count;
    }
    private void For(SourceSpan span)
    {
        string name=Name();Need("=");var start=Expr();Need("TO");var end=Expr();var step=Eat("STEP")?Expr():new BLiteral((short)1,span);
        int initial=code.Instructions.Count;var init=Emit("For",span,name,[start,end,step]);var exits=new List<BInstruction>();loops.Push(("FOR",exits));
        Block("NEXT");Need("NEXT");if(!EndStatement){string closing=Name();if(!closing.Equals(name,StringComparison.OrdinalIgnoreCase))throw BLexer.Error("NEXT variable mismatch.",span);}
        var next=Emit("Next",span,name);next.Link=initial;next.Jump=initial+1;init.Jump=code.Instructions.Count;foreach(var exit in exits)exit.Jump=init.Jump;loops.Pop();
    }
    private void Loop(SourceSpan span,bool whileLoop)
    {
        int top=code.Instructions.Count;BInstruction? test=null;
        if(whileLoop)test=Emit("If",span,args:[Expr()]);else if(Eat("WHILE"))test=Emit("If",span,args:[Expr()]);else if(Eat("UNTIL"))test=Emit("If",span,args:[new BUnary("NOT",Expr(),span)]);
        int bodyStart=code.Instructions.Count;
        var exits=new List<BInstruction>();loops.Push((whileLoop?"WHILE":"DO",exits));Block(whileLoop?"WEND":"LOOP");Need(whileLoop?"WEND":"LOOP");
        bool empty=code.Instructions.Count==bodyStart;
        if(test is not null)test.TimedWait=empty&&IsTimerWait(test.Args[0]);
        if(!whileLoop&&(At("WHILE")||At("UNTIL")))
        {bool until=Eat("UNTIL");if(!until)Need("WHILE");var condition=Expr();var bottom=Emit("If",span,args:[until?new BUnary("NOT",condition,span):condition]);bottom.Jump=code.Instructions.Count+1;bottom.TimedWait=empty&&IsTimerWait(bottom.Args[0]);}
        Emit("Jump",span).Jump=top;int end=code.Instructions.Count;if(test is not null)test.Jump=end;foreach(var exit in exits)exit.Jump=end;loops.Pop();
    }
    private void Select(SourceSpan span)
    {
        int selector=code.Instructions.Count;Emit("Select",span,args:[Expr()]);Need("\n");Separators();List<BInstruction> ends=[];
        while(Eat("CASE"))
        {
            List<(string Op,BExpr A,BExpr? B)> tests=[];bool otherwise=Eat("ELSE");
            if(!otherwise)do{string op=Eat("IS")?Take().Text:"=";var a=Expr();tests.Add(Eat("TO")?("RANGE",a,Expr()):(op,a,null));}while(Eat(","));
            var test=Emit("Case",span,data:tests);test.Link=selector;if(!Eat(":"))Need("\n");Block("CASE","END SELECT");ends.Add(Emit("Jump",span));test.Jump=code.Instructions.Count;
            if(otherwise)break;
        }
        Need("END");Need("SELECT");foreach(var end in ends)end.Jump=code.Instructions.Count;
    }
    private BExpr Postfix()
    {
        BExpr result;
        if(Eat("(")){result=Expr();Need(")");}
        else{var current=Take();result=current.Literal is not null?new BLiteral(current.Literal,current.Span):new BName(current.Text,current.Span);if(current.Is("<eof>")||current.Is("\n"))throw BLexer.Error("Expected expression.",current.Span);}
        while(true)
        {
            if(Eat(".")){result=new BMember(result,Name(),result.Span);continue;}
            if(Eat("(")){List<BExpr> args=[];if(!Eat(")")){do{args.Add(Expr());}while(Eat(","));Need(")");}result=new BCall(result,args,result.Span);continue;}
            return result;
        }
    }
    private BExpr Expr(int minimum=0)
    {
        if(++depth>128)throw BLexer.Error("Expression nesting128 exceeded.",Current.Span);
        try
        {
            BExpr result;if(At("+")||At("-")||At("NOT")){var op=Take();result=new BUnary(op.Text.ToUpperInvariant(),Expr(op.Is("NOT")?4:9),op.Span);}else result=Postfix();
            while(true)
            {
                string op=Current.Text.ToUpperInvariant();int precedence=op switch{"OR"=>1,"XOR"=>2,"AND"=>3,"=" or "<>" or "<" or ">" or "<=" or ">="=>4,"+" or "-"=>5,"MOD"=>6,"\\"=>7,"*" or "/"=>8,"^"=>10,_=>-1};
                if(precedence<minimum)break;Take();result=new BBinary(op,result,Expr(op=="^"?precedence:precedence+1),result.Span);
            }
            return result;
        }
        finally{depth--;}
    }
}
