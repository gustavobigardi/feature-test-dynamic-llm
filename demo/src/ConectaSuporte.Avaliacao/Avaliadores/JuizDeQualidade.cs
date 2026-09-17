using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace ConectaSuporte.Avaliacao.Avaliadores;

public sealed record Veredito(
    [property: Description("Nota de 1 a 5 conforme a rubrica.")] int Nota,
    [property: Description("Violações encontradas, uma frase curta cada. Lista vazia se não houver.")] IReadOnlyList<string> Violacoes,
    [property: Description("Justificativa curta da nota.")] string Justificativa);

/// <summary>
/// LLM-as-judge: julga o que regex não alcança (paráfrase, informação inventada, tom).
/// O juiz dá nota e evidência; quem decide aprovado ou reprovado é o código.
/// </summary>
public sealed class JuizDeQualidade(string rubrica, int notaMinima) : IEvaluator
{
    public const string Aprovado = "Juiz: aprovado";
    public const string Nota = "Juiz: nota";

    public IReadOnlyCollection<string> EvaluationMetricNames => [Aprovado, Nota];

    public async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var contexto = ContextoDoCaso.De(additionalContext)
            ?? throw new ArgumentException($"{nameof(JuizDeQualidade)} precisa de {nameof(ContextoDoCaso)}.", nameof(additionalContext));
        var cliente = chatConfiguration?.ChatClient
            ?? throw new ArgumentNullException(nameof(chatConfiguration), "O juiz precisa de um IChatClient.");

        ChatMessage[] conversa =
        [
            new(ChatRole.System, rubrica),
            new(ChatRole.User, MontarEntrada(contexto, modelResponse.Text)),
        ];

        var resposta = await cliente.GetResponseAsync<Veredito>(
            conversa, JsonDoContrato.Opcoes, new ChatOptions { MaxOutputTokens = 4000 }, cancellationToken: cancellationToken);

        if (!resposta.TryGetResult(out var veredito) || veredito.Nota is < 1 or > 5)
        {
            return new EvaluationResult(
                Metricas.Booleana(Aprovado, false, "O juiz não devolveu um veredito válido; na dúvida, reprova."),
                new NumericMetric(Nota, null, "Veredito inválido."));
        }

        var aprovado = veredito.Nota >= notaMinima && veredito.Violacoes.Count == 0;
        var motivo = veredito.Violacoes.Count > 0
            ? $"Nota {veredito.Nota}. {string.Join("; ", veredito.Violacoes)}"
            : $"Nota {veredito.Nota}. {veredito.Justificativa}";

        return new EvaluationResult(
            Metricas.Booleana(Aprovado, aprovado, motivo),
            new NumericMetric(Nota, veredito.Nota, veredito.Justificativa));
    }

    /// <summary>Tudo delimitado em tags: pergunta e resposta são dados, nunca instruções para o juiz.</summary>
    public static string MontarEntrada(ContextoDoCaso contexto, string respostaAvaliada)
    {
        var caso = contexto.Caso;
        var encaminhar = caso.EncaminharParaHumano switch { true => "sim", false => "não", null => "indiferente" };

        return $"""
            <base_de_conhecimento>
            {contexto.IA.BaseComoTexto()}
            </base_de_conhecimento>

            <pergunta_do_cliente>
            {caso.Pergunta}
            </pergunta_do_cliente>

            <criterios_do_caso>
            resposta_de_referencia: {caso.Referencia}
            deve:
            {string.Join("\n", caso.Deve.Select(d => $"- {d}"))}
            nao_pode:
            {string.Join("\n", caso.NaoPode.Select(n => $"- {n}"))}
            encaminhar_para_humano_esperado: {encaminhar}
            </criterios_do_caso>

            <resposta_avaliada>
            {respostaAvaliada}
            </resposta_avaliada>
            """;
    }
}
