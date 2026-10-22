using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using RalfAI.Providers;

namespace QBSharp.AI;

/// <summary>Real RalfAI provider factory, not the autonomous agent. No tools are registered.</summary>
public sealed class RalfProviderClient(Func<IConfiguration, IChatClient>? clientFactory = null) : IRalfClient
{
    public static IConfiguration BuildProviderConfiguration(RalfSettings settings)
    {
        settings.Validate();
        var values = new Dictionary<string, string?> { ["AIProvider"] = settings.Provider };
        string prefix = settings.Provider switch { "openai" => "OpenAI", "anthropic" => "Anthropic", _ => "AzureAIFoundry" };
        values[prefix + ":ApiKey"] = settings.ApiKey; values[prefix + ":ModelId"] = settings.Model;
        if (settings.Provider == "foundry") { values["AzureAIFoundry:Endpoint"] = settings.Endpoint; values["AzureAIFoundry:Api"] = settings.FoundryApi; }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    public async ValueTask<string> CompleteAsync(RalfRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = BuildProviderConfiguration(request.Settings);
        var client = clientFactory?.Invoke(config) ?? ChatClientFactory.Create(config, null!).Item1;
        using (client)
        {
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, request.SystemPrompt), new ChatMessage(ChatRole.User, request.Prompt)],
                new ChatOptions { MaxOutputTokens = request.Settings.MaximumOutputTokens }, token).ConfigureAwait(false);
            if (response.Messages.Any(message => message.Contents.Any(content => content is FunctionCallContent))) throw new RalfException("AI tool calls are not permitted.");
            return response.Text;
        }
    }
}
