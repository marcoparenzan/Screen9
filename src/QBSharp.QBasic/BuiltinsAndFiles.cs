using System.Globalization;
using System.Text;

namespace QBSharp.QBasic;

internal sealed class BFiles(QBasicOptions options)
{
    private sealed class Handle(StreamReader? reader,StreamWriter? writer){public StreamReader? Reader { get; }=reader;public StreamWriter? Writer { get; }=writer;}
    private readonly Dictionary<int,Handle> handles=[];
    private string PathFor(string path)
    {if(!options.AllowFileIO)throw new BFault(70,"Numbered file I/O disabled by host.");return Path.GetFullPath(path,Path.GetFullPath(options.BaseDirectory));}
    private Handle Get(int number)=>handles.TryGetValue(number,out var handle)?handle:throw new BFault(52,"File number not open.");
    public void Open(string path,int number,string mode)
    {
        if(number is <1 or >255||handles.ContainsKey(number)||handles.Count>=16)throw new BFault(52,"Invalid, duplicate or excessive file number.");
        path=PathFor(path);if(File.Exists(path)&&new FileInfo(path).Length>1_048_576)throw new BFault(7,"File size limit1MiB.");
        handles[number]=mode switch
        {
            "INPUT"=>new(new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read),new UTF8Encoding(false,true)),null),
            "OUTPUT"=>new(null,new StreamWriter(new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read),new UTF8Encoding(false,true))),
            "APPEND"=>new(null,new StreamWriter(new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read),new UTF8Encoding(false,true))),
            _=>throw new BFault(54,"Only INPUT/OUTPUT/APPEND files supported.")
        };
    }
    public void Close(int number){var handle=Get(number);handle.Reader?.Dispose();handle.Writer?.Dispose();handles.Remove(number);}
    public void CloseAll(){foreach(var number in handles.Keys.ToArray())Close(number);}
    public bool Eof(int number)=> (Get(number).Reader??throw new BFault(54,"File not open for input.")).Peek()<0;
    public string Read(int number,bool line,bool stringValue)
    {
        var reader=Get(number).Reader??throw new BFault(54,"File not open for input.");
        if(line)return reader.ReadLine()??throw new BFault(62,"Input past end of file.");
        return ReadField(reader,stringValue);
    }
    public void Write(int number,string text)
    {
        var writer=Get(number).Writer??throw new BFault(54,"File not open for output.");
        if(writer.BaseStream.Length+new UTF8Encoding(false,true).GetByteCount(text)>1_048_576)throw new BFault(7,"File size limit1MiB.");writer.Write(text);writer.Flush();
    }
    public void Delete(string path)=>File.Delete(PathFor(path));
    public static string ReadField(TextReader reader,bool stringValue)
    {
        while(reader.Peek()>=0&&(char.IsWhiteSpace((char)reader.Peek())||reader.Peek()==','))reader.Read();
        if(reader.Peek()<0)throw new BFault(62,"Input past end of file.");
        var value=new StringBuilder();bool quoted=reader.Peek()=='"';if(quoted)reader.Read();
        while(reader.Peek()>=0)
        {
            int c=reader.Peek();
            if(quoted&&c=='"'){reader.Read();if(reader.Peek()=='"'){reader.Read();value.Append('"');continue;}break;}
            if(!quoted&&(c==','||c is '\r' or '\n'||!stringValue&&char.IsWhiteSpace((char)c)))break;
            value.Append((char)reader.Read());if(value.Length>65536)throw new BFault(7,"Input field limit.");
        }
        if(reader.Peek()==','||reader.Peek()=='\r'||reader.Peek()=='\n'){int c=reader.Read();if(c=='\r'&&reader.Peek()=='\n')reader.Read();}
        return quoted?value.ToString():value.ToString().Trim();
    }
    public static double ParseNumber(string text)
    {if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||!double.IsFinite(value))throw new BFault(13,"Invalid numeric input/DATA.");return value;}
}
internal sealed record BTab(int Column);
internal sealed partial class QBasicSession
{
    private static readonly HashSet<string> Builtins=new("ABS INP ATN SIN COS TAN SQR INT FIX CINT CLNG CSNG CDBL SGN LEN LEFT RIGHT MID LTRIM RTRIM UCASE LCASE STR VAL CHR ASC INSTR STRING SPACE SPC TAB RND TIMER ERR ERL EOF LBOUND UBOUND PEEK INKEY POINT PLAY".Split(' '),StringComparer.OrdinalIgnoreCase);
    private static bool BuiltinExists(string name)=>Builtins.Contains(BNames.Base(name));
    private static bool BuiltinZero(string name)=>BNames.Base(name).ToUpperInvariant() is "INKEY" or "TIMER" or "RND" or "ERR" or "ERL";
    private object? Builtin(string name,List<object?> args)
    {
        name=BNames.Base(name).ToUpperInvariant();
        void Count(int min,int max){if(args.Count<min||args.Count>max)throw new BFault(5,"Builtin argument count mismatch.");}
        double N(int i)=>BValues.Number(args[i]);int I(int i)=>BValues.Integer(args[i]);string S(int i)=>args[i] as string??throw new BFault(13,"String argument required.");
        switch(name)
        {
            
            case "PLAY":Count(1,1);return (short)(speaker?.Notes??0); // notes still in the background music buffer
            case "PEEK":Count(1,1);return (short)memory.Peek(I(0),graphics);
            case "INP":Count(1,1);int port=I(0);if(port is <0 or >65535)throw new BFault(5,"INP port 0..65535.");return (short)Port(()=>Graphics.ReadPort(port),port);
            case "POINT":Count(2,2);return Graphics.Point(I(0),I(1));
            case "ERR":Count(0,0);return errorNumber;case "ERL":Count(0,0);return errorLine;
            case "TIMER":Count(0,0);return (float)options.Clock().TimeOfDay.TotalSeconds;
            case "RND":Count(0,1);if(args.Count>0){if(N(0)==0)return lastRandom;if(N(0)<0)random=new Random(BitConverter.SingleToInt32Bits((float)N(0)));}return lastRandom=(float)random.NextDouble();
            case "EOF":Count(1,1);return BValues.Boolean(files.Eof(I(0)));
            case "LBOUND":case "UBOUND":Count(1,2);if(args[0] is not QBasicArray array)throw new BFault(13,"Array required.");int dimension=args.Count==2?I(1)-1:0;if(dimension<0||dimension>=array.Rank)throw new BFault(9,"Invalid dimension.");return name=="LBOUND"?array.GetLowerBound(dimension):array.GetUpperBound(dimension);
            case "ABS":Count(1,1);return Math.Abs(N(0));case "ATN":Count(1,1);return Math.Atan(N(0));case "SIN":Count(1,1);return Math.Sin(N(0));case "COS":Count(1,1);return Math.Cos(N(0));case "TAN":Count(1,1);return Math.Tan(N(0));
            case "SQR":Count(1,1);if(N(0)<0)throw new BFault(5,"Negative square root.");return Math.Sqrt(N(0));
            case "INT":Count(1,1);return Math.Floor(N(0));case "FIX":Count(1,1);return Math.Truncate(N(0));case "CINT":Count(1,1);return checked((short)I(0));case "CLNG":Count(1,1);return I(0);case "CSNG":Count(1,1);return ConvertValue(args[0],"SINGLE");case "CDBL":Count(1,1);return ConvertValue(args[0],"DOUBLE");case "SGN":Count(1,1);return (short)Math.Sign(N(0));
            case "LEN":Count(1,1);return S(0).Length;
            case "LEFT":case "RIGHT":Count(2,2);if(I(1)<0)throw new BFault(5,"Negative string length.");int length=Math.Min(I(1),S(0).Length);return name=="LEFT"?S(0)[..length]:S(0)[^length..];
            case "MID":Count(2,3);int start=I(1)-1;if(start<0||args.Count==3&&I(2)<0)throw new BFault(5,"Invalid MID arguments.");if(start>=S(0).Length)return "";return S(0).Substring(start,args.Count==3?Math.Min(I(2),S(0).Length-start):S(0).Length-start);
            case "LTRIM":Count(1,1);return S(0).TrimStart(' ');case "RTRIM":Count(1,1);return S(0).TrimEnd(' ');case "UCASE":Count(1,1);return S(0).ToUpperInvariant();case "LCASE":Count(1,1);return S(0).ToLowerInvariant();
            case "STR":Count(1,1);return (N(0)>=0?" ":"")+BValues.Text(args[0]);
            case "VAL":Count(1,1);string input=S(0).TrimStart();int end=0;while(end<input.Length&&"0123456789+-.eEdD".Contains(input[end]))end++;return double.TryParse(input[..end].Replace('D','E').Replace('d','E'),NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed)&&double.IsFinite(parsed)?parsed:0d;
            case "CHR":Count(1,1);if(I(0) is <0 or >255)throw new BFault(5,"CHR range0..255.");return ((char)I(0)).ToString();case "ASC":Count(1,1);if(S(0).Length==0)throw new BFault(5,"ASC requires nonempty text.");return (short)S(0)[0];
            case "INSTR":Count(2,3);int offset=args.Count==3?I(0)-1:0;if(offset<0)throw new BFault(5,"INSTR starts at1.");string haystack=args.Count==3?S(1):S(0),needle=args.Count==3?S(2):S(1);return offset>haystack.Length?0:haystack.IndexOf(needle,offset,StringComparison.Ordinal)+1;
            case "STRING":Count(2,2);int count=I(0);if(count is <0 or >65536)throw new BFault(5,"STRING length range0..65536.");char c=args[1] is string str?str.Length>0?str[0]:throw new BFault(5,"Character required."):(char)I(1);return new string(c,count);
            case "SPACE":case "SPC":Count(1,1);if(I(0) is <0 or >65536)throw new BFault(5,"Space limit.");return new string(' ',I(0));
            case "TAB":Count(1,1);if(I(0) is <1 or >65536)throw new BFault(5,"TAB position must be positive.");return new BTab(I(0));
        }
        throw new BFault(5,"Unknown builtin.");
    }
    private async ValueTask Print(BInstruction node,BScope scope)
    {
        var (file,parts)=((BExpr? File,List<(BExpr? Value,string Separator)> Parts))node.Data!;
        int? number=file is null?null:BValues.Integer(await Eval(file,scope));var output=new StringBuilder();int column=number is null?printColumn:0;
        foreach(var part in parts)
        {
            object? value=part.Value is null?null:await Eval(part.Value,scope);string text;
            if(node.Op=="WriteFile")text=value is string s?"\""+s.Replace("\"","\"\"")+"\"":BValues.Text(value);
            else if(value is BTab tab){if(column>=tab.Column){output.AppendLine();column=0;}int padding=Math.Max(0,tab.Column-1-(column>=tab.Column?0:column));text=new string(' ',padding);}
            else text=BValues.Numeric(value)?(BValues.Number(value)>=0?" ":"")+BValues.Text(value)+" ":BValues.Text(value);
            output.Append(text);if(number is null)AdvanceColumn(text,ref column);else column+=text.Length;
            if(part.Separator==",")
            {string separator=node.Op=="WriteFile"?",":new string(' ',14-column%14);output.Append(separator);if(number is null)AdvanceColumn(separator,ref column);else column+=separator.Length;}
        }
        if(parts.Count==0||parts[^1].Separator.Length==0||node.Op=="WriteFile"){output.AppendLine();column=0;}
        if(number is { } id)files.Write(id,output.ToString());else Display(output.ToString());
    }
    private void Display(string text)
    {
        textDisplay.Write(text,foreground,background,graphics,cancellation);
        if(graphics is not null)options.PresentGraphics?.Invoke(graphics);else if(options.PresentText is not null)options.PresentText(textDisplay.Cells);else{options.Output.Write(text);options.Output.Flush();}
        printColumn=textDisplay.Column-1;
    }
    /// <summary>DOS-style line editing on the screen: printable keys echo at the cursor, Backspace erases, Enter ends (max 255 characters).</summary>
    private async ValueTask<string> ScreenReadLine(string prompt)
    {
        if(prompt.Length>0)Display(prompt);
        var text=new StringBuilder();
        while(true)
        {
            string key=await PollKeyAsync();
            if(key.Length==0){if(options.InputPollingInterval==TimeSpan.Zero&&options.StepBudgetMode!=QBasicStepBudgetMode.BetweenInputPolls)await options.Delay(TimeSpan.FromMilliseconds(10),cancellation);continue;}
            if(key=="\r"){Display("\n");return text.ToString();}
            if(key=="\b")
            {
                if(text.Length==0)continue;text.Length--;textDisplay.Backspace(foreground,background,graphics);
                if(graphics is not null)options.PresentGraphics?.Invoke(graphics);else options.PresentText?.Invoke(textDisplay.Cells);
                printColumn=textDisplay.Column-1;continue;
            }
            if(key.Length!=1||char.IsControl(key[0])||text.Length>=255)continue;
            text.Append(key);Display(key);
        }
    }
    private void AdvanceColumn(string text,ref int column)
    {
        foreach(char character in text)
        {
            if(character is '\r' or '\n'){column=0;continue;}
            if(column>=textWidth)column=0;
            column++;
        }
    }
    // A completed console line is not a file stream: empty answers are valid.
    // Keep numbered-file EOF detection strict and confined to BFiles.ReadField.
    private static string ReadConsoleField(TextReader reader,bool stringValue)
    {
        while(reader.Peek()>=0&&char.IsWhiteSpace((char)reader.Peek()))reader.Read();
        if(reader.Peek()<0)return stringValue?"":"0";
        if(reader.Peek()==','){reader.Read();return stringValue?"":"0";}
        return BFiles.ReadField(reader,stringValue);
    }

    private async ValueTask Input(BInstruction node,BScope scope)
    {
        var (file,prompt,question)=((BExpr? File,BExpr? Prompt,bool Question))node.Data!;
        int? number=file is null?null:BValues.Integer(await Eval(file,scope));
        string promptText=prompt is null?"":BValues.Text(await Eval(prompt,scope));
        TextReader? consoleLine=null;
        if(number is null&&options.ScreenInput)consoleLine=new StringReader(await ScreenReadLine(promptText+(question?"? ":"")));
        else if(promptText.Length>0)options.Output.Write(promptText);
        if(number is null&&consoleLine is null)
        {
            cancellation.ThrowIfCancellationRequested();string? line;
            try{line=options.Input.ReadLine();}
            catch(Exception) when(cancellation.IsCancellationRequested){throw new OperationCanceledException(cancellation);}
            cancellation.ThrowIfCancellationRequested();
            consoleLine=new StringReader(line??throw new BFault(62,"Console input ended."));
        }
        try
        {
            foreach(var expression in node.Args)
            {
                cancellation.ThrowIfCancellationRequested();var cell=await Reference(expression,scope);bool text=cell.Type.StartsWith("STRING",StringComparison.OrdinalIgnoreCase);
                string value=number is { } id?files.Read(id,node.Op=="LineInput",text):node.Op=="LineInput"?consoleLine!.ReadToEnd():ReadConsoleField(consoleLine!,text);
                cell.Value=text?value:BFiles.ParseNumber(value);
            }
        }
        finally{consoleLine?.Dispose();}
    }
}
