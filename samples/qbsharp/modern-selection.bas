' BASIC moderno: classi, For Each, ElseIf e Select Case
Class Counter
    Property Total As Integer
    Sub Add(items)
        For Each value As Integer In items
            If value < 0 Then
                Me.Total = Me.Total - value
            ElseIf value = 0 Then
                Me.Total = Me.Total
            Else
                Me.Total = Me.Total + value
            End If
        Next value
    End Sub
End Class
Dim items = New List(Of Integer)()
items.Add(-2)
items.Add(0)
items.Add(3)
Dim instance = New Counter()
instance.Add(items)
AssertEqual(5, instance.Total)
Dim label As String = ""
Select Case instance.Total
Case Is < 0
    label = "negative"
Case 0, 1
    label = "small"
Case 2 To 9
    label = "ready"
Case Else
    label = "large"
End Select
AssertEqual("ready", label)
Print("Modern selection + classes: " & label)
