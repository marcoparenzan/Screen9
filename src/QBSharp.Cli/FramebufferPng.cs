using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Screen9;

namespace QBSharp.Cli;

/// <summary>Lossless native-resolution RGB diagnostic image of the visible PC page.</summary>
public static class FramebufferPng
{
    public static void Save(PCCanvas canvas, string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        byte[] pixels = canvas.PixelSnapshot(canvas.VisualPage);
        using var image = new MemoryStream();
        image.Write(new byte[] {137,80,78,71,13,10,26,10});
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header,canvas.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),canvas.Height);
        header[8]=8; header[9]=2;
        Chunk(image,"IHDR",header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed,CompressionLevel.Fastest,true))
        {
            byte[] row=new byte[checked(canvas.Width*3+1)];
            for(int y=0;y<canvas.Height;y++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=0;x<canvas.Width;x++)
                {
                    int rgb=canvas.GetRgb(pixels[y*canvas.Width+x]).Packed;
                    row[x*3+1]=(byte)(rgb>>16);row[x*3+2]=(byte)(rgb>>8);row[x*3+3]=(byte)rgb;
                }
                zlib.Write(row);
            }
        }
        Chunk(image,"IDAT",compressed.ToArray());Chunk(image,"IEND",[]);
        string full=Path.GetFullPath(path),temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            token.ThrowIfCancellationRequested();
            File.WriteAllBytes(temp,image.ToArray());
            token.ThrowIfCancellationRequested();File.Move(temp,full,true);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    private static void Chunk(Stream stream,string type,byte[] data)
    {
        Span<byte> number=stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number,data.Length);stream.Write(number);
        byte[] name=Encoding.ASCII.GetBytes(type);stream.Write(name);stream.Write(data);
        uint crc=uint.MaxValue;
        foreach(byte b in name)crc=Update(crc,b);
        foreach(byte b in data)crc=Update(crc,b);
        BinaryPrimitives.WriteUInt32BigEndian(number,~crc);stream.Write(number);
    }
    private static uint Update(uint crc,byte value)
    {
        crc^=value;
        for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);
        return crc;
    }
}
