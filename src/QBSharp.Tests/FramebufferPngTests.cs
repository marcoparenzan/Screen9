using System.Buffers.Binary;
using System.IO.Compression;
using Screen9;
using QBSharp.Cli;
namespace QBSharp.Tests;
public sealed class FramebufferPngTests
{
    [Fact]
    public void NativeResolutionVisiblePageAndPaletteArePreserved()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        try
        {
            var pc=new PCCanvas(7);pc.ActivePage=1;pc.VisualPage=1;pc.PSet(0,0,4);
            FramebufferPng.Save(pc,path);
            byte[] file=File.ReadAllBytes(path);
            Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},file[..8]);
            Assert.Equal(pc.Width,BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(16)));
            Assert.Equal(pc.Height,BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(20)));
            using var compressed=new MemoryStream();
            for(int p=8;p<file.Length;)
            {
                int length=BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(p));
                if(System.Text.Encoding.ASCII.GetString(file,p+4,4)=="IDAT")compressed.Write(file,p+8,length);
                p+=length+12;
            }
            compressed.Position=0;using var z=new ZLibStream(compressed,CompressionMode.Decompress);
            using var raw=new MemoryStream();z.CopyTo(raw);byte[] pixels=raw.ToArray();
            Assert.Equal((pc.Width*3+1)*pc.Height,pixels.Length);
            int rgb=pc.GetRgb(4).Packed;
            Assert.Equal((byte)(rgb>>16),pixels[1]);Assert.Equal((byte)(rgb>>8),pixels[2]);Assert.Equal((byte)rgb,pixels[3]);
            FramebufferPng.Save(pc,path); // atomic overwrite
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void CancellationDoesNotCreateFile()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        Assert.Throws<OperationCanceledException>(()=>FramebufferPng.Save(new PCCanvas(13),path,new CancellationToken(true)));
        Assert.False(File.Exists(path));
    }
}
