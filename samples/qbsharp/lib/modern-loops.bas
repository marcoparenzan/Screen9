Namespace Ops
    Class Accumulator
        Property Total As Integer = 0
        Function Sum(upper As Integer) As Integer
            Total = 0
            For i As Integer = 1 To upper
                Dim item = i
                Total = Total + item
            Next i
            Return Total
        End Function
    End Class
End Namespace
