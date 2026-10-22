' AI opt-in: configure QBS_AI_* environment before running. May incur provider charges.
Dim answer = Ralf("Spiega brevemente cosa fa un deploy blue-green")
Print(answer.Text)
Print(answer.Provider & ":" & answer.Model)
