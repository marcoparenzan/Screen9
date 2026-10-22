using System.Globalization;

namespace Screen9;

/// <summary>A PC-speaker event. Frequency zero denotes silence. No hardware is accessed by the engine.</summary>
public sealed record PCTone(double Frequency, TimeSpan Duration, bool Background = false);

/// <summary>Bounded PC-speaker music parser. Supports notes/rests, N, O, L, T, octave shifts and M modes; no variable expansion.</summary>
public sealed class PCMusic
{
    private int octave=4, length=4, tempo=120;
    private double articulation=.875;
    private bool background;
    public IReadOnlyList<PCTone> Parse(string source)
    {
        if(source.Length>4096)throw new ArgumentException("PLAY source exceeds 4096 characters.");
        source=source.ToUpperInvariant();List<PCTone> tones=[];int index=0;
        int Number(int fallback)
        {int start=index;while(index<source.Length&&char.IsAsciiDigit(source[index]))index++;return start==index?fallback:int.Parse(source[start..index],CultureInfo.InvariantCulture);}
        void Note(int semitone,int denominator)
        {
            if(denominator is <1 or >64)throw new ArgumentException("PLAY note length must be 1..64.");
            double duration=240d/tempo/denominator,extra=duration;
            while(index<source.Length&&source[index]=='.'){index++;extra/=2;duration+=extra;}
            double sound=semitone<0?0:440*Math.Pow(2,(semitone-57)/12d);
            tones.Add(new(sound,TimeSpan.FromSeconds(duration*(semitone<0?1:articulation)),background));
            if(semitone>=0&&articulation<1)tones.Add(new(0,TimeSpan.FromSeconds(duration*(1-articulation)),background));
            if(tones.Count>1024)throw new ArgumentException("PLAY event limit exceeded.");
        }
        while(index<source.Length)
        {
            char command=source[index++];if(char.IsWhiteSpace(command))continue;
            switch(command)
            {
                case 'O':octave=Number(-1);if(octave is <0 or >6)throw new ArgumentException("PLAY octave 0..6.");break;
                case 'L':length=Number(-1);if(length is <1 or >64)throw new ArgumentException("PLAY length 1..64.");break;
                case 'T':tempo=Number(-1);if(tempo is <32 or >255)throw new ArgumentException("PLAY tempo 32..255.");break;
                case '<':octave=Math.Max(0,octave-1);break;case '>':octave=Math.Min(6,octave+1);break;
                case 'M':if(index>=source.Length)throw new ArgumentException("PLAY mode missing.");switch(source[index++])
                    {case 'B':background=true;break;case 'F':background=false;break;case 'N':articulation=.875;break;case 'L':articulation=1;break;case 'S':articulation=.75;break;default:throw new ArgumentException("Unknown PLAY mode.");}break;
                case 'P':Note(-1,Number(length));break;
                case 'N':int n=Number(-1);if(n is <0 or >84)throw new ArgumentException("PLAY N 0..84.");Note(n==0?-1:n-1,length);break;
                case 'A':case 'B':case 'C':case 'D':case 'E':case 'F':case 'G':
                    int pitch=command switch{'C'=>0,'D'=>2,'E'=>4,'F'=>5,'G'=>7,'A'=>9,_=>11};
                    if(index<source.Length&&source[index] is '#' or '+' or '-')pitch+=source[index++]=='-'?-1:1;
                    Note(octave*12+pitch,Number(length));break;
                default:throw new ArgumentException("Unsupported PLAY command: "+command);
            }
        }
        return tones;
    }
}
