using System.Globalization;
using ConectaSuporte.Avaliacao.Dataset;

namespace ConectaSuporte.Avaliacao.Portao;

public sealed record ResultadoAmostra(
    string CasoId,
    string Variante,
    int Amostra,
    bool Aprovada,
    double? NotaJuiz,
    IReadOnlyList<string> Falhas,
    string Resposta);

public enum Decisao
{
    Aprovado,
    Inconclusivo,
    Piorou,
    FalhaCritica,
    AbaixoDoMinimo,
}

public sealed record ResultadoPortao(
    Decisao Decisao,
    string Motivo,
    double TaxaCandidato,
    double? TaxaBaseline,
    double? Delta,
    double? IcInferior,
    double? IcSuperior,
    IReadOnlyList<ResultadoAmostra> FalhasCriticas)
{
    public bool Bloqueia(bool inconclusivoBloqueia) => Decisao switch
    {
        Decisao.Aprovado => false,
        Decisao.Inconclusivo => inconclusivoBloqueia,
        _ => true,
    };
}

public static class PortaoDeQualidade
{
    public static ResultadoPortao Decidir(
        IReadOnlyList<CasoDourado> casos,
        IReadOnlyList<ResultadoAmostra> candidato,
        IReadOnlyList<ResultadoAmostra>? baseline,
        RegraDeDecisao regra)
    {
        var taxaCandidato = TaxaGeral(casos, candidato);
        double? taxaBaseline = baseline is null ? null : TaxaGeral(casos, baseline);

        // 1. Tolerância zero: média alta não compensa promessa indevida, vazamento ou dado exposto.
        var criticos = casos.Where(c => c.Critico).Select(c => c.Id).ToHashSet();
        var falhasCriticas = candidato
            .Where(a => criticos.Contains(a.CasoId) && !a.Aprovada)
            .OrderBy(a => a.CasoId, StringComparer.Ordinal).ThenBy(a => a.Amostra)
            .ToList();

        if (falhasCriticas.Count > 0)
        {
            var casosAfetados = falhasCriticas.Select(f => f.CasoId).Distinct().Count();
            return new ResultadoPortao(Decisao.FalhaCritica,
                $"{falhasCriticas.Count} amostra(s) reprovada(s) em {casosAfetados} caso(s) crítico(s). Média nenhuma compensa isso.",
                taxaCandidato, taxaBaseline, taxaCandidato - taxaBaseline, null, null, falhasCriticas);
        }

        if (baseline is null)
        {
            return taxaCandidato < regra.TaxaMinima
                ? new ResultadoPortao(Decisao.AbaixoDoMinimo, AbaixoDoMinimo(taxaCandidato, regra), taxaCandidato, null, null, null, null, [])
                : new ResultadoPortao(Decisao.Aprovado, "Sem baseline para comparar: valem só os critérios absolutos.", taxaCandidato, null, null, null, null, []);
        }

        // 2. Score é distribuição: a unidade reamostrada é o caso, com suas amostras juntas.
        var deltas = casos.Select(c => Taxa(candidato, c.Id) - Taxa(baseline, c.Id)).ToArray();
        var (inferior, superior) = Bootstrap.IntervaloDaMedia(deltas, regra.Reamostras, regra.Confianca, regra.Semente);
        var intervalo = $"[{Formato.PontosPercentuais(inferior)}; {Formato.PontosPercentuais(superior)}]";
        var margem = Formato.PontosPercentuais(-regra.Margem);

        ResultadoPortao Com(Decisao decisao, string motivo) =>
            new(decisao, motivo, taxaCandidato, taxaBaseline, deltas.Average(), inferior, superior, []);

        // 3. Piorou = mesmo no melhor cenário plausível, a perda passa da margem.
        if (superior < -regra.Margem)
            return Com(Decisao.Piorou, $"Com {regra.Confianca.ToString("P0", Formato.PtBr)} de confiança, o candidato perde mais que a margem tolerada ({margem}). IC da diferença: {intervalo}.");

        if (taxaCandidato < regra.TaxaMinima)
            return Com(Decisao.AbaixoDoMinimo, AbaixoDoMinimo(taxaCandidato, regra));

        // 4. Aprovado = mesmo no pior cenário plausível, a perda fica dentro da margem.
        return inferior >= -regra.Margem
            ? Com(Decisao.Aprovado, $"Mesmo no pior cenário plausível ({Formato.PontosPercentuais(inferior)}), a perda fica dentro da margem tolerada ({margem}).")
            : Com(Decisao.Inconclusivo, $"O intervalo {intervalo} cruza a margem ({margem}): a amostra não basta para afirmar que piorou nem que não piorou. Rode o nível completo.");
    }

    private static string AbaixoDoMinimo(double taxa, RegraDeDecisao regra) =>
        $"Taxa de aprovação de {Formato.Percentual(taxa)}, abaixo do mínimo absoluto de {Formato.Percentual(regra.TaxaMinima)}.";

    /// <summary>Média das taxas por caso: cada caso pesa igual.</summary>
    public static double TaxaGeral(IReadOnlyList<CasoDourado> casos, IReadOnlyList<ResultadoAmostra> amostras) =>
        casos.Average(c => Taxa(amostras, c.Id));

    public static double Taxa(IReadOnlyList<ResultadoAmostra> amostras, string casoId)
    {
        var doCaso = amostras.Where(a => a.CasoId == casoId).ToList();
        if (doCaso.Count == 0)
            throw new InvalidOperationException($"Nenhuma amostra para o caso {casoId}: execução incompleta não é aprovação.");

        return doCaso.Average(a => a.Aprovada ? 1d : 0d);
    }
}

public static class Bootstrap
{
    /// <summary>Intervalo de confiança percentil da média, com semente fixa para o cálculo ser reproduzível.</summary>
    public static (double Inferior, double Superior) IntervaloDaMedia(IReadOnlyList<double> valores, int reamostras, double confianca, int semente)
    {
        if (valores.Count == 0)
            throw new ArgumentException("Sem valores para reamostrar.", nameof(valores));

        var aleatorio = new Random(semente);
        var medias = new double[reamostras];

        for (var i = 0; i < reamostras; i++)
        {
            var soma = 0d;
            for (var j = 0; j < valores.Count; j++)
                soma += valores[aleatorio.Next(valores.Count)];

            medias[i] = soma / valores.Count;
        }

        Array.Sort(medias);
        var alfa = (1 - confianca) / 2;
        return (Percentil(medias, alfa), Percentil(medias, 1 - alfa));
    }

    private static double Percentil(double[] ordenados, double p)
    {
        var posicao = p * (ordenados.Length - 1);
        var abaixo = (int)Math.Floor(posicao);
        var acima = (int)Math.Ceiling(posicao);
        return ordenados[abaixo] + (ordenados[acima] - ordenados[abaixo]) * (posicao - abaixo);
    }
}

public static class Formato
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Percentual(double valor) => (valor * 100).ToString("0.0", PtBr) + "%";

    public static string PontosPercentuais(double valor) => (valor * 100).ToString("+0.0;−0.0;0.0", PtBr) + " p.p.";

    public static string Numero(long valor) => valor.ToString("N0", PtBr);
}
