using System.Text;

namespace QBSharp.Cli;

/// <summary>Interactive line input shares the key-event path with INKEY$, avoiding Console.In EOF after native key polling.</summary>
public sealed class QBasicInteractiveReader(Func<string> readKey,TextWriter output,CancellationToken token,bool ansi=true):TextReader
{
    public override string? ReadLine()
    {
        token.ThrowIfCancellationRequested();var text=new StringBuilder();
        if(ansi)output.Write("\x1b[?7h\x1b[?25h");
        output.WriteLine();output.Flush();
        try
        {
            while(true)
            {
                token.ThrowIfCancellationRequested();string key=readKey();token.ThrowIfCancellationRequested();
                if(key.Length==0){Thread.Sleep(10);continue;}
                if(key=="\r"){output.WriteLine();output.Flush();return text.ToString();}
                if(key=="\x1a" && text.Length==0)return null;
                if(key=="\b")
                {if(text.Length>0){text.Length--;output.Write("\b \b");output.Flush();}continue;}
                if(key.Length!=1 || char.IsControl(key[0]))continue;
                if(text.Length>=65536)throw new InvalidDataException("QBasic input line exceeds 65536 characters.");
                text.Append(key);output.Write(key);output.Flush();
            }
        }
        finally{if(ansi){output.Write("\x1b[?25l\x1b[?7l");output.Flush();}}
    }
}
