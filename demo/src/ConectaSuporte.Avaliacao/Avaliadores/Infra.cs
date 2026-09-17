using System.Text.Json;
using ConectaSuporte.Avaliacao.Dataset;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace ConectaSuporte.Avaliacao.Avaliadores;

/// <summary>Leva o caso do dataset e o pacote de IA avaliado até os avaliadores.</summary>
public sealed class ContextoDoCaso(CasoDourado caso, ConfiguracaoIA ia)
    : EvaluationContext("Caso do dataset dourado", $"{caso.Id}: {caso.Pergunta}")
{
    public CasoDourado Caso { get; } = caso;

    public ConfiguracaoIA IA { get; } = ia;

    public static ContextoDoCaso? De(IEnumerable<EvaluationContext>? contextos) =>
        contextos?.OfType<ContextoDoCaso>().FirstOrDefault();
}

internal static class RespostaEstruturada
{
    public static RespostaSuporte? Ler(ChatResponse resposta)
    {
        if (resposta is ChatResponse<RespostaSuporte> tipada && tipada.TryGetResult(out var resultado))
            return resultado;

        try
        {
            return JsonSerializer.Deserialize<RespostaSuporte>(resposta.Text, JsonDoContrato.Opcoes);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static class Metricas
{
    public static BooleanMetric Regra(string nome, IReadOnlyCollection<string> problemas) =>
        Booleana(nome, problemas.Count == 0, problemas.Count == 0 ? "OK" : string.Join("; ", problemas));

    public static BooleanMetric NaoSeAplica(string nome, string motivo) => Booleana(nome, null, motivo);

    public static BooleanMetric Booleana(string nome, bool? passou, string motivo) => new(nome, passou, motivo)
    {
        Interpretation = passou switch
        {
            true => new EvaluationMetricInterpretation(EvaluationRating.Good, false, "Passou."),
            false => new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, true, motivo),
            null => new EvaluationMetricInterpretation(EvaluationRating.Inconclusive, false, motivo),
        },
    };
}
