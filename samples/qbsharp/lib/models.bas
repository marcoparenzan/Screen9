Namespace Ops
    Public Class Counter
        Public Property Value As Integer
        Public Sub New(initial As Integer)
            Me.Value = initial
        End Sub
        Public Sub Add(amount As Integer)
            Value = Value + amount
        End Sub
        Public Function Twice() As Integer
            Return Value * 2
        End Function
    End Class
End Namespace
