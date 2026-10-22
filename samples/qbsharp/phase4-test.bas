Import "lib/models" As Models
Dim counter As Models.Ops.Counter = New Models.Ops.Counter(20)
counter.Add(1)
AssertEqual(42, counter.Twice())
Dim data As Dictionary(Of String, Integer) = New Dictionary(Of String, Integer)()
data.Add("value", counter.Value)
AssertEqual(21, data.get_Item("value"))
Import "lib/models" As Again
Dim other As Again.Ops.Counter = New Again.Ops.Counter(1)
AssertEqual(2, other.Twice())
