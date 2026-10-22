using System.Globalization;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.QBasic;

internal sealed record BToken(string Text, object? Literal, SourceSpan Span)
{
    public bool Is(string value) => Text.Equals(value, StringComparison.OrdinalIgnoreCase);
}
internal abstract record BExpr(SourceSpan Span);
internal sealed record BLiteral(object? Value, SourceSpan Source) : BExpr(Source);
internal sealed record BName(string Name, SourceSpan Source) : BExpr(Source);
internal sealed record BUnary(string Op, BExpr Value, SourceSpan Source) : BExpr(Source);
internal sealed record BBinary(string Op, BExpr Left, BExpr Right, SourceSpan Source) : BExpr(Source);
internal sealed record BCall(BExpr Target, List<BExpr> Args, SourceSpan Source) : BExpr(Source);
internal sealed record BMember(BExpr Target, string Name, SourceSpan Source) : BExpr(Source);
internal sealed record BBound(BExpr? Lower, BExpr Upper);
internal sealed record BVariable(string Name, string? Type, List<BBound>? Bounds, bool Shared = false);
internal sealed record BParameter(string Name, string? Type, bool Array, bool ByValue);
internal sealed record BProcedure(string Name, string? Type, bool Function, List<BParameter> Parameters, BCode Code);
internal sealed class BInstruction(string op, SourceSpan span, string name = "", List<BExpr>? args = null, object? data = null)
{
    public string Op { get; } = op;
    public SourceSpan Span { get; } = span;
    public string Name { get; } = name;
    public List<BExpr> Args { get; } = args ?? [];
    public object? Data { get; } = data;
    public int Jump { get; set; } = -1;
    public int Link { get; set; } = -1;
    public bool TimedWait { get; set; }
}
internal sealed class BCode
{
    public List<BInstruction> Instructions { get; } = [];
    public Dictionary<string,int> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
}
internal sealed class BProgram(BCode main)
{
    public BCode Main { get; } = main;
    public Dictionary<string,BProcedure> Procedures { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,List<BVariable>> Types { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<object?> Data { get; } = [];
    public Dictionary<string,int> DataLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeclaredFunctions { get; } = new(StringComparer.OrdinalIgnoreCase);
}
internal static class BNames
{
    public static string Base(string name) => name.TrimEnd('$','%','&','!','#');
    public static string? Suffix(string name) => name.Length == 0 ? null : name[^1] switch { '$'=>"STRING",'%'=>"INTEGER",'&'=>"LONG",'!'=>"SINGLE",'#'=>"DOUBLE",_=>null };
}
internal static class BLexer
{
    public static List<BToken> Scan(ScriptSource source, CancellationToken token)
    {
        string text=source.Text; if(text.Length>1_048_576)throw Error("Source limit1Mi characters exceeded.",new(source.FileName,new(0,1,1),0));
        List<BToken> result=[]; int pos=0,line=1,col=1;
        void Move(){pos++;col++;}
        SourceSpan Span(int start,int row,int column)=>new(source.FileName,new(start,row,column),pos-start);
        while(pos<text.Length)
        {
            token.ThrowIfCancellationRequested();int start=pos,row=line,column=col;char c=text[pos];
            if(c is '\r' or '\n'){Move();if(c=='\r'&&pos<text.Length&&text[pos]=='\n')Move();result.Add(new("\n",null,Span(start,row,column)));line++;col=1;continue;}
            if(char.IsWhiteSpace(c)){Move();continue;}
            if(c=='\''){while(pos<text.Length&&text[pos] is not ('\r' or '\n'))Move();continue;}
            if(char.IsLetter(c)||c=='_')
            {
                Move();while(pos<text.Length&&(char.IsLetterOrDigit(text[pos])||text[pos]=='_'))Move();
                if(pos<text.Length&&"$%&!#".Contains(text[pos]))Move();
                string name=text[start..pos];
                if(name.Equals("REM",StringComparison.OrdinalIgnoreCase)){while(pos<text.Length&&text[pos] is not ('\r' or '\n'))Move();continue;}
                result.Add(new(name,null,Span(start,row,column)));continue;
            }
            if(c=='"')
            {
                Move();var value=new System.Text.StringBuilder();bool closed=false;
                while(pos<text.Length&&text[pos] is not ('\r' or '\n'))
                {
                    if(text[pos]=='"'){Move();if(pos<text.Length&&text[pos]=='"'){value.Append('"');Move();}else{closed=true;break;}}
                    else{value.Append(text[pos]);Move();}
                }
                if(!closed)throw Error("Unterminated string.",Span(start,row,column));
                result.Add(new("<string>",value.ToString(),Span(start,row,column)));continue;
            }
            if(char.IsAsciiDigit(c)||c=='.'&&pos+1<text.Length&&char.IsAsciiDigit(text[pos+1]))
            {
                bool floating=false,doublePrecision=false;
                while(pos<text.Length&&char.IsAsciiDigit(text[pos]))Move();
                if(pos<text.Length&&text[pos]=='.'){floating=true;Move();while(pos<text.Length&&char.IsAsciiDigit(text[pos]))Move();}
                if(pos<text.Length&&char.ToUpperInvariant(text[pos]) is 'E' or 'D')
                {floating=true;doublePrecision=char.ToUpperInvariant(text[pos])=='D';Move();if(pos<text.Length&&text[pos] is '+' or '-')Move();while(pos<text.Length&&char.IsAsciiDigit(text[pos]))Move();}
                string raw=text[start..pos].Replace('D','E').Replace('d','E'); char suffix=pos<text.Length&&"%&!#".Contains(text[pos])?text[pos]: '\0';if(suffix!='\0')Move();
                object value;
                try
                {
                    if(floating||suffix is '!' or '#'){double number=double.Parse(raw,NumberStyles.Float,CultureInfo.InvariantCulture);if(!double.IsFinite(number))throw new OverflowException();value=suffix=='#'||doublePrecision?(object)number:(float)number;if(value is float f&&!float.IsFinite(f))throw new OverflowException();}
                    else{long n=long.Parse(raw,CultureInfo.InvariantCulture);value=suffix=='%'?(object)checked((short)n):suffix=='&'?(object)checked((int)n):n<=short.MaxValue?(object)(short)n:checked((int)n);}
                }
                catch(Exception e) when(e is FormatException or OverflowException){throw Error("Numeric literal invalid or out of range.",Span(start,row,column));}
                result.Add(new("<number>",value,Span(start,row,column)));continue;
            }
            if(c=='&'&&pos+1<text.Length&&char.ToUpperInvariant(text[pos+1]) is 'H' or 'O')
            {
                Move();int radix=char.ToUpperInvariant(text[pos])=='H'?16:8;Move();int digits=pos;
                while(pos<text.Length&&Uri.IsHexDigit(text[pos]))Move();
                try{long n=Convert.ToInt64(text[digits..pos],radix);if(pos<text.Length&&text[pos]=='&')Move();result.Add(new("<number>",n<=short.MaxValue?(object)(short)n:checked((int)n),Span(start,row,column)));}
                catch(Exception){throw Error("Invalid hex/octal literal.",Span(start,row,column));}continue;
            }
            Move();if(c is '<' or '>'&&pos<text.Length&&(text[pos]=='='||c=='<'&&text[pos]=='>'))Move();
            if(!"+-*/\\^=<>(),.:;#".Contains(c))throw Error($"Unexpected character '{c}'.",Span(start,row,column));
            result.Add(new(text[start..pos],null,Span(start,row,column)));
        }
        result.Add(new("<eof>",null,new(source.FileName,new(pos,line,col),0)));return result;
    }
    internal static ScriptRuntimeException Error(string message,SourceSpan span)=>new(new ScriptDiagnostic("QBS1400",message,span));
}
