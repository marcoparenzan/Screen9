# LocalFeed

Feed NuGet locale del repository: i pacchetti che Screen9 usa e che non sono (ancora) su nuget.org, così
`dotnet restore` funziona senza altra configurazione. Registrato in `src/Directory.Build.props`.

| Pacchetto | Versione | Origine |
|---|---|---|
| `PySharp.Interpreter` | 2.5.0 | https://github.com/marcoparenzan/PySharp (`dotnet pack src/PySharpLib -c Release -o LocalFeed`) |

Per aggiornare un pacchetto: rifare il `dotnet pack` dal repository sorgente verso questa cartella, cambiare la
versione nel progetto che lo referenzia, e togliere il `.nupkg` vecchio. Chi preferisce un feed proprio può
crearlo dallo stesso repository e aggiungerlo alle fonti NuGet: questa cartella è solo la scorciatoia.
