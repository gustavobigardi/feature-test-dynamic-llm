using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ConectaSuporte;

/// <summary>Cria o IChatClient do Azure OpenAI (API v1) a partir de variáveis de ambiente.</summary>
public static class ClienteModelo
{
    public static bool Configurado =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"));

    /// <param name="implantacao">Nome da implantação (deployment) no recurso do Azure OpenAI.</param>
    public static IChatClient Criar(string implantacao)
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Defina AZURE_OPENAI_ENDPOINT, por exemplo https://meu-recurso.openai.azure.com.");

        var opcoes = new OpenAIClientOptions { Endpoint = new Uri($"{endpoint.TrimEnd('/')}/openai/v1/") };
        var chave = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        // Sem chave, usa Entra ID: az login na sua máquina, OIDC (azure/login) no GitHub Actions.
        var cliente = string.IsNullOrWhiteSpace(chave)
            ? new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default"), opcoes)
            : new OpenAIClient(new ApiKeyCredential(chave), opcoes);

        return cliente.GetChatClient(implantacao).AsIChatClient();
    }
}
