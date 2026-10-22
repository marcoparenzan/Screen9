' Filesystem test: uses a fresh caller-provided directory; never run against valuable data.
Class Job
    Property Name As String
    Property Count As Integer
    Function Description() As String
        Return Name & ":" & Count
    End Function
End Class
Dim jobValue As Job = Json.Deserialize("{""Name"":""deploy"",""Count"":42}", "Job")
AssertEqual("deploy:42", jobValue.Description())
Dim restored As Job = Yaml.Deserialize(Yaml.Serialize(jobValue), "Job")
AssertEqual(42, restored.Count)
Dim folder = Args.GetValue(0)
Assert(Not Directory.Exists(folder), "Use a fresh directory for this example")
Directory.Create(folder)
Dim jsonPath = Path.Combine(folder, "job.json")
Dim yamlPath = Path.Combine(folder, "job.yaml")
File.WriteJson(jsonPath, jobValue)
File.WriteYaml(yamlPath, jobValue)
Dim fromJson As Job = File.ReadJson(jsonPath, "Job")
Dim fromYaml As Job = File.ReadYaml(yamlPath, "Job")
AssertEqual(42, fromJson.Count)
AssertEqual("deploy", fromYaml.Name)
AssertEqual(1, Directory.Glob("*.json", folder).Length)
File.WriteText(Path.Combine(folder, "note.txt"), "città")
File.Copy(Path.Combine(folder, "note.txt"), Path.Combine(folder, "copy.txt"))
AssertEqual("città", File.ReadText(Path.Combine(folder, "copy.txt")))
Directory.Delete(folder, True)
Assert(Not Directory.Exists(folder))
