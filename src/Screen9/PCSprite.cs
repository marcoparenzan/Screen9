using System.Buffers.Binary;

namespace Screen9;

/// <summary>PUT raster operation: how sprite pixels combine with the framebuffer.</summary>
public enum PCSpriteOperation { PSet, PReset, Xor, And, Or }

/// <summary>QBasic GET/PUT LONG-array format: little endian width/height header; packed CGA or row-interleaved EGA planes.
/// Works on 32-bit words (a QBasic LONG array from its lower bound).</summary>
public static class PCSprite
{
    private static int Bits(PCCanvas canvas) => canvas.Width switch
    {
        _ when canvas.Mode==1 => 2,
        _ when canvas.Mode is 2 or 11 => 1,
        _ when canvas.Mode==13 => 8,
        _ => 4
    };
    private static int RowBytes(int width,int bits)=>bits==4?((width+7)/8)*4:(width*bits+7)/8;
    /// <summary>GET: stores the rectangle (corners inclusive) into <paramref name="words"/>; returns the words written, the rest is untouched.</summary>
    public static int Capture(PCCanvas canvas,Span<int> words,int x1,int y1,int x2,int y2)
    {
        int left=Math.Min(x1,x2),top=Math.Min(y1,y2),width=Math.Abs(x2-x1)+1,height=Math.Abs(y2-y1)+1,bits=Bits(canvas);
        if(left<0||top<0||left+width>canvas.Width||top+height>canvas.Height)throw new ArgumentException("GET rectangle outside framebuffer.");
        int stride=RowBytes(width,bits);var bytes=new byte[checked(4+stride*height)];if(bytes.Length>words.Length*4)throw new ArgumentException("GET buffer too small.");
        BinaryPrimitives.WriteUInt16LittleEndian(bytes,(ushort)(bits==2?width*2:width));BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2),(ushort)height);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int color=canvas.Point(left+x,top+y),row=4+y*stride;
            if(bits==4)for(int plane=0;plane<4;plane++){if((color&(1<<plane))!=0)bytes[row+plane*((width+7)/8)+x/8]|=(byte)(128>>(x%8));}
            else if(bits==8)bytes[row+x]=(byte)color;
            else bytes[row+x*bits/8]|=(byte)(color<<(8-bits-(x*bits%8)));
        }
        var padded=new byte[(bytes.Length+3)/4*4];bytes.CopyTo(padded,0);
        for(int i=0;i<padded.Length/4;i++)words[i]=BinaryPrimitives.ReadInt32LittleEndian(padded.AsSpan(i*4,4));
        return padded.Length/4;
    }
    /// <summary>PUT: draws the sprite in <paramref name="words"/> at (left, top), clipping pixels outside the framebuffer.</summary>
    public static void Draw(PCCanvas canvas,ReadOnlySpan<int> words,int left,int top,PCSpriteOperation operation=PCSpriteOperation.Xor,CancellationToken cancellationToken=default)
    {
        var bytes=new byte[checked(words.Length*4)];
        for(int i=0;i<words.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*4,4),words[i]);
        if(bytes.Length<4)throw new ArgumentException("Missing sprite header.");
        int bits=Bits(canvas),width=BinaryPrimitives.ReadUInt16LittleEndian(bytes),height=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
        if(bits==2){if(width%2!=0)throw new ArgumentException("Invalid CGA sprite width.");width/=2;}
        if(width<1||height<1||width>canvas.Width||height>canvas.Height)throw new ArgumentException("Invalid sprite dimensions.");
        int stride=RowBytes(width,bits);if(4+stride*height>bytes.Length)throw new ArgumentException("Truncated sprite buffer.");
        if(!Enum.IsDefined(operation))throw new ArgumentOutOfRangeException(nameof(operation));
        int mask=(1<<bits)-1;
        for(int y=0;y<height;y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for(int x=0;x<width;x++)
            {
                int row=4+y*stride,color=0;
                if(bits==4)for(int plane=0;plane<4;plane++){if((bytes[row+plane*((width+7)/8)+x/8]&(128>>(x%8)))!=0)color|=1<<plane;}
                else color=bits==8?bytes[row+x]:(bytes[row+x*bits/8]>>(8-bits-(x*bits%8)))&mask;
                int xx=left+x,yy=top+y;if(xx<0||yy<0||xx>=canvas.Width||yy>=canvas.Height)continue;
                int old=canvas.Point(xx,yy);
                canvas.PSet(xx,yy,operation switch{PCSpriteOperation.PSet=>color,PCSpriteOperation.PReset=>color^mask,PCSpriteOperation.Xor=>old^color,PCSpriteOperation.And=>old&color,_=>old|color});
            }
        }
    }
    /// <summary>GET into an array: same as the span overload, for script hosts that bind by reflection (spans are not reachable there).</summary>
    public static int Capture(PCCanvas canvas,int[] words,int x1,int y1,int x2,int y2)=>Capture(canvas,words.AsSpan(),x1,y1,x2,y2);
    /// <summary>PUT from an array: same as the span overload, for script hosts that bind by reflection.</summary>
    public static void Draw(PCCanvas canvas,int[] words,int left,int top,PCSpriteOperation operation=PCSpriteOperation.Xor,CancellationToken cancellationToken=default)
        =>Draw(canvas,(ReadOnlySpan<int>)words,left,top,operation,cancellationToken);
    /// <summary>Parses a QBasic PUT action name (PSET, PRESET, XOR, AND, OR; case-insensitive).</summary>
    public static PCSpriteOperation ParseOperation(string name)=>name.ToUpperInvariant() switch
    {
        "PSET"=>PCSpriteOperation.PSet,"PRESET"=>PCSpriteOperation.PReset,"XOR"=>PCSpriteOperation.Xor,"AND"=>PCSpriteOperation.And,"OR"=>PCSpriteOperation.Or,
        _=>throw new ArgumentException("Invalid PUT operation.")
    };
}
