' qbs test assertions.bas -- sample
Assert(2 + 3 * 4 = 14, "precedenza")
AssertEqual(42, 6 * 7)
AssertEqual(1, Args.Length)
AssertEqual("sample", Args.GetValue(0))
