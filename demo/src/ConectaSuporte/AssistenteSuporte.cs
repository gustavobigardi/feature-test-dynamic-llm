using Microsoft.Extensions.AI;

namespace ConectaSuporte;

/// <param name="Mensagens">Conversa enviada ao modelo (system + pergunta).</param>
/// <param name="Bruta">Resposta do modelo como veio, para os avaliadores.</param>
/// <param name="Estruturada">Resposta no contrato, ou null se o modelo não o respeitou.</param>
public sealed record RespostaDoAssistente(IReadOnlyList<ChatMessage> Mensagens, ChatResponse Bruta, RespostaSuporte? Estruturada);

/// <summary>A feature: uma pergunta de cliente vira uma resposta estruturada.</summary>
public sealed class AssistenteSuporte(IChatClient chatClient, ConfiguracaoIA ia)
{
    public async Task<RespostaDoAssistente> ResponderAsync(string pergunta, CancellationToken cancellationToken = default)
    {
        var mensagens = ia.MontarMensagens(pergunta);

        var resposta = await chatClient.GetResponseAsync<RespostaSuporte>(
            mensagens, JsonDoContrato.Opcoes, ia.CriarOpcoes(), cancellationToken: cancellationToken);

        return new RespostaDoAssistente(mensagens, resposta, resposta.TryGetResult(out var estruturada) ? estruturada : null);
    }
}
