Import "lib/models" As Models
Dim counter As Models.Ops.Counter = New Models.Ops.Counter(20)
counter.Add(1)
Dim numbers As List(Of Integer) = New List(Of Integer)()
numbers.Add(counter.Twice())
Print("class=" & numbers.get_Item(0))
