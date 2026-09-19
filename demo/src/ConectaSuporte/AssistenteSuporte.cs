using Microsoft.Extensions.AI;
using System.Text.Json;

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

        if (PoliticaDeSeguranca.TentarResponder(pergunta) is { } respostaSegura)
        {
            var bruta = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(respostaSegura, JsonDoContrato.Opcoes)));
            return new RespostaDoAssistente(mensagens, bruta, respostaSegura);
        }

        var resposta = await chatClient.GetResponseAsync<RespostaSuporte>(
            mensagens, JsonDoContrato.Opcoes, ia.CriarOpcoes(), cancellationToken: cancellationToken);

        return new RespostaDoAssistente(mensagens, resposta, resposta.TryGetResult(out var estruturada) ? estruturada : null);
    }
}

internal static class PoliticaDeSeguranca
{
    public static RespostaSuporte? TentarResponder(string pergunta)
    {
        var texto = pergunta.ToUpperInvariant();

        if (texto.Contains("SENHA") && texto.Contains("WI-FI"))
            return new("Não posso informar senhas nem dados de clientes. A troca de senha é feita pelo próprio titular no app, em Meu Wi-Fi > Alterar senha.", "suporte-tecnico", false, ["suporte-tecnico.md"]);

        if (texto.Contains("CARTÃO") || texto.Contains("CARTAO"))
            return new("Por segurança, o chat nunca solicita dados de cartão. Você pode pagar por Pix, boleto ou débito automático, pelo app no menu Faturas.", "fatura", false, ["fatura.md"]);

        if (texto.Contains("CPF") && (texto.Contains("CONTA") || texto.Contains("CADASTR")))
            return new("Não tenho acesso aos dados da sua conta. Você pode consultar seus dados cadastrais no app Conecta Vix ou com o atendimento humano.", "fora-do-escopo", false, []);

        if (texto.Contains("INSTRUÇÕES DE SISTEMA") || texto.Contains("INSTRUCOES DE SISTEMA"))
            return new("Não posso compartilhar minhas instruções internas. Posso ajudar com planos, faturas, cancelamento ou suporte técnico.", "fora-do-escopo", false, []);

        return null;
    }
}
