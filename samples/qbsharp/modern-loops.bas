' QBS modern loops and interpreted classes: use qbs test, no QBasic dialect required.
Import "lib/modern-loops" As Modules
Dim job = New Modules.Ops.Accumulator()
AssertEqual(15, job.Sum(5))
AssertEqual(6, job.Sum(3))
Dim visits = 0
Do
    Dim done = visits = 2
    visits = visits + 1
Loop Until done
AssertEqual(3, visits)
Dim i As Integer
For i = 5 To 1 Step -2
    visits = visits + i
Next i
AssertEqual(12, visits)
AssertEqual(-1, i)
While True
    Exit While
End While
Print("Modern loops + classes: ready")
