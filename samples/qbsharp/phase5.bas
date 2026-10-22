' Standard library: data-only demo, no files created.
Class Job
    Property Name As String = "deploy"
    Property Count As Integer
    Function Description() As String
        Return Name & ":" & Count
    End Function
End Class
Dim jobValue As Job = Json.Deserialize("{""Name"":""deploy"",""Count"":42}", "Job")
Dim yamlText = Yaml.Serialize(jobValue)
Dim restored As Job = Yaml.Deserialize(yamlText, "Job")
Print(restored.Description())
Ui.Window("QBSharp", "JSON/YAML round-trip completato")
Dim rows = Json.Deserialize("[{""Name"":""deploy"",""Count"":42}]")
Ui.Table(rows)
Ui.Progress(100, "Round-trip")
