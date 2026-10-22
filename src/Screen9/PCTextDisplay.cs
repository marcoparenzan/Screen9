namespace Screen9;

/// <summary>Text cells plus raster glyphs: IBM ROM fonts from Screen9 (PCFont.ForHeight; FontDirectories searched first with PCFont.Find), compact 5x7 for cells not 8 pixels wide.</summary>
public sealed class PCTextDisplay
{
    public Canvas Cells { get; private set; }=new(80,25);
    public int Row { get; private set; }=1;
    public int Column { get; private set; }=1;
    public bool UseIbmFonts { get; set; }
    /// <summary>Folders searched with <see cref="PCFont.Find"/> before the built-in ROM fonts (null entries skipped).</summary>
    public string?[] FontDirectories { get; set; }=[];
    private int first=1,last=25;
    private static readonly Dictionary<char,string> Glyphs=new()
    {
        ['A']="01110/10001/10001/11111/10001/10001/10001",['B']="11110/10001/10001/11110/10001/10001/11110",
        ['C']="01111/10000/10000/10000/10000/10000/01111",['D']="11110/10001/10001/10001/10001/10001/11110",
        ['E']="11111/10000/10000/11110/10000/10000/11111",['F']="11111/10000/10000/11110/10000/10000/10000",
        ['G']="01111/10000/10000/10111/10001/10001/01111",['H']="10001/10001/10001/11111/10001/10001/10001",
        ['I']="11111/00100/00100/00100/00100/00100/11111",['J']="00111/00010/00010/00010/00010/10010/01100",
        ['K']="10001/10010/10100/11000/10100/10010/10001",['L']="10000/10000/10000/10000/10000/10000/11111",
        ['M']="10001/11011/10101/10101/10001/10001/10001",['N']="10001/11001/10101/10011/10001/10001/10001",
        ['O']="01110/10001/10001/10001/10001/10001/01110",['P']="11110/10001/10001/11110/10000/10000/10000",
        ['Q']="01110/10001/10001/10001/10101/10010/01101",['R']="11110/10001/10001/11110/10100/10010/10001",
        ['S']="01111/10000/10000/01110/00001/00001/11110",['T']="11111/00100/00100/00100/00100/00100/00100",
        ['U']="10001/10001/10001/10001/10001/10001/01110",['V']="10001/10001/10001/10001/10001/01010/00100",
        ['W']="10001/10001/10001/10101/10101/10101/01010",['X']="10001/10001/01010/00100/01010/10001/10001",
        ['Y']="10001/10001/01010/00100/00100/00100/00100",['Z']="11111/00001/00010/00100/01000/10000/11111",
        ['0']="01110/10001/10011/10101/11001/10001/01110",['1']="00100/01100/00100/00100/00100/00100/01110",
        ['2']="01110/10001/00001/00010/00100/01000/11111",['3']="11110/00001/00001/01110/00001/00001/11110",
        ['4']="00010/00110/01010/10010/11111/00010/00010",['5']="11111/10000/10000/11110/00001/00001/11110",
        ['6']="01110/10000/10000/11110/10001/10001/01110",['7']="11111/00001/00010/00100/01000/01000/01000",
        ['8']="01110/10001/10001/01110/10001/10001/01110",['9']="01110/10001/10001/01111/00001/00001/01110",
        ['.']="00000/00000/00000/00000/00000/00110/00110",[',']="00000/00000/00000/00000/00110/00110/00100",
        [':']="00000/00110/00110/00000/00110/00110/00000",[';']="00000/00110/00110/00000/00110/00110/00100",
        ['-']="00000/00000/00000/11111/00000/00000/00000",['_']="00000/00000/00000/00000/00000/00000/11111",
        ['=']="00000/00000/11111/00000/11111/00000/00000",['+']="00000/00100/00100/11111/00100/00100/00000",
        ['?']="01110/10001/00001/00010/00100/00000/00100",['!']="00100/00100/00100/00100/00100/00000/00100",
        ['(']="00010/00100/01000/01000/01000/00100/00010",[')']="01000/00100/00010/00010/00010/00100/01000",
        ['[']="01110/01000/01000/01000/01000/01000/01110",[']']="01110/00010/00010/00010/00010/00010/01110",
        ['/']="00001/00001/00010/00100/01000/10000/10000",['*']="00000/10101/01110/11111/01110/10101/00000",
        ['"']="01010/01010/01010/00000/00000/00000/00000",['\'']="00100/00100/00100/00000/00000/00000/00000"
    };
    public void Resize(int width,int height)
    {if(width is <1 or >300||height is <1 or >100)throw new ArgumentException("Text dimensions out of range.");Cells=new(width,height);first=1;last=height;Row=Column=1;}
    public void Locate(int? row,int? column)
    {int y=row??Row,x=column??Column;if(y<1||y>Cells.Height||x<1||x>Cells.Width)throw new ArgumentException("Invalid LOCATE position.");Row=y;Column=x;}
    public void View(int? top,int? bottom)
    {int a=top??1,b=bottom??Cells.Height;if(a<1||b<a||b>Cells.Height)throw new ArgumentException("Invalid VIEW PRINT range.");first=a;last=b;Locate(a,1);}
    public void Clear(int foreground,int background,PCCanvas? graphics,bool regionOnly,CancellationToken cancellationToken=default)
    {
        int top=regionOnly?first:1,bottom=regionOnly?last:Cells.Height;
        for(int y=top-1;y<bottom;y++)for(int x=0;x<Cells.Width;x++){cancellationToken.ThrowIfCancellationRequested();Cells.SetPixel(x,y,new(' ',(ConsoleColor)foreground,(ConsoleColor)background));Raster(graphics,x,y,' ',foreground,background);}
        Locate(top,1);
    }
    public void Write(string text,int foreground,int background,PCCanvas? graphics,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested(); foreach(char c in text)
        {
            cancellationToken.ThrowIfCancellationRequested(); if(c=='\r'){Column=1;continue;}
            if(c=='\n'){NewLine(graphics,foreground,background,cancellationToken);continue;}
            if(Column>Cells.Width)NewLine(graphics,foreground,background,cancellationToken);
            char glyph=char.IsControl(c)?'?':c;
            Cells.SetPixel(Column-1,Row-1,new(glyph,(ConsoleColor)foreground,(ConsoleColor)background));
            Raster(graphics,Column-1,Row-1,HasCp437Glyph(c)?c:glyph,foreground,background);Column++;
        }
    }
    /// <summary>Control codes QBasic shows as CP437 symbols (☺ ♥ ► ⌂...) rather than interpreting; only the IBM raster draws them.</summary>
    private bool HasCp437Glyph(char c)=>UseIbmFonts&&(c is >='\x01' and <='\x1b' and not ('\a' or '\t' or '\v' or '\f')||c=='\x7f');
    /// <summary>Moves the cursor back one cell (to the end of the previous row at column 1) and blanks it, without scrolling.</summary>
    public void Backspace(int foreground,int background,PCCanvas? graphics)
    {
        if(Column>1)Column--;else if(Row>first){Row--;Column=Cells.Width;}else return;
        Cells.SetPixel(Column-1,Row-1,new(' ',(ConsoleColor)foreground,(ConsoleColor)background));Raster(graphics,Column-1,Row-1,' ',foreground,background);
    }
    private void NewLine(PCCanvas? graphics,int fg,int bg,CancellationToken cancellationToken)
    {
        Column=1;Row++;if(Row<=last)return;Row=last;
        for(int y=first-1;y<last-1;y++)for(int x=0;x<Cells.Width;x++)Cells.SetPixel(x,y,Cells[x,y+1]);
        for(int x=0;x<Cells.Width;x++)Cells.SetPixel(x,last-1,new(' ',(ConsoleColor)fg,(ConsoleColor)bg));
        if(graphics is not null)
        {
            int height=graphics.Height/Cells.Height;cancellationToken.ThrowIfCancellationRequested();
            graphics.ScrollUp((first-1)*height,(last-first+1)*height,height,bg);
        }
    }
    private void Raster(PCCanvas? graphics,int cellX,int cellY,char glyph,int fg,int bg)
    {
        if(graphics is null)return;int w=graphics.Width/Cells.Width,h=graphics.Height/Cells.Height;
        var font=UseIbmFonts&&w==8?(FontDirectories.Length>0?PCFont.Find(h,FontDirectories):null)??(h is 8 or 14 or 16?PCFont.ForHeight(h):null):null;
        if(font is not null){graphics.DrawGlyph(cellX,cellY,glyph<256?glyph:'?',fg,bg,font);return;}
        string pattern=Glyphs.GetValueOrDefault(char.ToUpperInvariant(glyph),Glyphs['?']);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            bool ink=glyph!=' '&&x>0&&x<6&&pattern[(y*7/h)*6+x-1]=='1';
            if(glyph is 'Ä' or 'Í')ink=y==h/2;
            if(glyph is '³' or 'º')ink=x==w/2;
            if(glyph=='Û')ink=true;
            graphics.PSet(cellX*w+x,cellY*h+y,ink?fg:bg);
        }
    }
}
