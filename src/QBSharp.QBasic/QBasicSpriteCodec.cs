using Screen9;

namespace QBSharp.QBasic;

/// <summary>GET/PUT on a QBasic LONG array: the format and raster operations are Screen9 <see cref="PCSprite"/>; this adapter copies the array words.</summary>
public static class QBasicSpriteCodec
{
    private static void Validate(QBasicArray array)
    {if(array.Rank!=1||array.ElementType!="LONG")throw new ArgumentException("Sprite buffer must be a one-dimensional LONG array.");}
    private static int[] Read(QBasicArray array)
    {
        Validate(array);var words=new int[array.Length];int lower=array.GetLowerBound(0);
        for(int i=0;i<words.Length;i++)words[i]=Convert.ToInt32(array.GetValue(lower+i));
        return words;
    }
    public static void Capture(PCCanvas canvas,QBasicArray array,int x1,int y1,int x2,int y2)
    {
        Validate(array);var words=new int[array.Length];int count=PCSprite.Capture(canvas,words,x1,y1,x2,y2),lower=array.GetLowerBound(0);
        for(int i=0;i<count;i++)array.SetValue(words[i],lower+i);
    }
    public static void Draw(PCCanvas canvas,QBasicArray array,int left,int top,string operation="XOR",CancellationToken cancellationToken=default)
        =>PCSprite.Draw(canvas,Read(array),left,top,PCSprite.ParseOperation(operation),cancellationToken);
}
