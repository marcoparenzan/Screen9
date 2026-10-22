' Logical EGA demo; no legacy QBasic syntax or DOS/BIOS calls.
Dim c = PC.Create(9)
PC.Clear(c)
PC.Box(c, 0, 200, 640, 150, 1, True)
PC.Box(c, 60, 140, 110, 210, 6, True)
PC.Box(c, 380, 100, 130, 250, 4, True)
PC.Circle(c, 320, 50, 28, 14)
PC.Paint(c, 320, 50, 14)
PC.Box(c, 90, 115, 20, 25, 2, True)
Dim sprite = PC.Get(c, 90, 115, 20, 25)
PC.Put(c, sprite, 200, 130, "xor")
Print("PCCanvas EGA " & c.Width & "x" & c.Height & " colors=" & c.ColorCount)
Dim renderNative = False
If Args.Length > 0 Then
    renderNative = Args.GetValue(0) = "render"
End If
If renderNative Then
    PC.Render(c, 80, 25)
Else
    Dim rows = PC.Ascii(c, 80, 25)
    Dim row = 0
    While row < rows.Length
        Print(rows.GetValue(row))
        row = row + 1
    End While
End If
