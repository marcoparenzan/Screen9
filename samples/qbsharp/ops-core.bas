' Phase 2 core operations; output is a host function.
Dim total As Integer = 0
Dim n As Integer = 5
While n > 0
    total = total + n
    n = n - 1
End While
Function Twice(value As Integer) As Integer
    Return value * 2
End Function
If total = 15 Then
    Print("total=" & Twice(total))
Else
    Print("unexpected")
End If
