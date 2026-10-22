' End-to-end fixture used with an injected offline fake, not a live provider.
Dim answer = Ralf("Return a JSON object with Select = 42", "json")
AssertEqual(42, answer.Data.Select)
AssertEqual("openai", answer.Provider)
AssertEqual("unit-model", answer.Model)
