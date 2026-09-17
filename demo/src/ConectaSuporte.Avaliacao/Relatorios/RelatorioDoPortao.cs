using System.Text;
using ConectaSuporte.Avaliacao.Calibracao;
using ConectaSuporte.Avaliacao.Dataset;
using ConectaSuporte.Avaliacao.Execucao;
using ConectaSuporte.Avaliacao.Portao;

namespace ConectaSuporte.Avaliacao.Relatorios;

public sealed record ResumoDaExecucao(
    string Nivel,
    int AmostrasPorCaso,
    IReadOnlyList<CasoDourado> Casos,
    VarianteAvaliada Candidato,
    IReadOnlyList<ResultadoAmostra> AmostrasCandidato,
    VarianteAvaliada? Baseline,
    IReadOnlyList<ResultadoAmostra>? AmostrasBaseline,
    ResultadoPortao Resultado,
    RegraDeDecisao Regra,
    ResultadoCalibracao Calibracao,
    string ModeloJuiz,
    UsoDoModelo Uso);

/// <summary>O relatório vai para o comentário do PR: quem lê precisa entender o porquê sem abrir log.</summary>
public static class RelatorioDoPortao
{
    public static string GerarMarkdown(ResumoDaExecucao r)
    {
        var resultado = r.Resultado;
        var bloqueia = resultado.Bloqueia(r.Regra.InconclusivoBloqueia);
        var md = new StringBuilder();

        md.AppendLine($"## {Icone(resultado.Decisao, bloqueia)} Portão de qualidade de IA: {Titulo(resultado.Decisao)}{(bloqueia ? " (merge bloqueado)" : string.Empty)}");
        md.AppendLine();
        md.AppendLine($"> {resultado.Motivo}");
        md.AppendLine();
        md.AppendLine($"Nível **{r.Nivel}**: {r.Casos.Count} casos × {r.AmostrasPorCaso} amostras por versão. " +
                      $"Juiz `{r.ModeloJuiz}` calibrado: concordância {Formato.Percentual(r.Calibracao.Concordancia.Taxa)}, κ {Kappa(r.Calibracao)}.");
        md.AppendLine();
        md.AppendLine("| | Baseline (main) | Candidato (esta mudança) |");
        md.AppendLine("|---|---:|---:|");
        md.AppendLine($"| Pacote de IA | {Versao(r.Baseline)} | {Versao(r.Candidato)} |");
        md.AppendLine($"| Taxa de aprovação | {(resultado.TaxaBaseline is { } tb ? Formato.Percentual(tb) : "—")} | {Formato.Percentual(resultado.TaxaCandidato)} |");
        md.AppendLine($"| Nota média do juiz | {NotaMedia(r.AmostrasBaseline)} | {NotaMedia(r.AmostrasCandidato)} |");
        md.AppendLine();

        if (resultado is { Delta: { } delta, IcInferior: { } inferior, IcSuperior: { } superior })
        {
            md.AppendLine($"**Diferença: {Formato.PontosPercentuais(delta)}** · IC {r.Regra.Confianca.ToString("P0", Formato.PtBr)} " +
                          $"[{Formato.PontosPercentuais(inferior)}; {Formato.PontosPercentuais(superior)}] · margem tolerada {Formato.PontosPercentuais(-r.Regra.Margem)}");
            md.AppendLine();
        }

        if (resultado.FalhasCriticas.Count > 0)
        {
            md.AppendLine("### Falhas críticas");
            md.AppendLine();
            md.AppendLine("| Caso | Amostra | O que falhou | Trecho da resposta |");
            md.AppendLine("|---|---:|---|---|");
            foreach (var falha in resultado.FalhasCriticas.Take(10))
                md.AppendLine($"| {falha.CasoId} | {falha.Amostra} | {Celula(string.Join("; ", falha.Falhas))} | {Celula(falha.Resposta)} |");
            md.AppendLine();
        }

        if (r.AmostrasBaseline is { } amostrasBaseline)
        {
            var pioras = r.Casos
                .Select(c => (Caso: c, Baseline: PortaoDeQualidade.Taxa(amostrasBaseline, c.Id), Candidato: PortaoDeQualidade.Taxa(r.AmostrasCandidato, c.Id)))
                .Where(x => x.Candidato < x.Baseline)
                .OrderBy(x => x.Candidato - x.Baseline).ThenBy(x => x.Caso.Id, StringComparer.Ordinal)
                .Take(5)
                .ToList();

            if (pioras.Count > 0)
            {
                md.AppendLine("### Casos que mais pioraram");
                md.AppendLine();
                md.AppendLine("| Caso | Tipo | Baseline | Candidato | Primeira falha no candidato |");
                md.AppendLine("|---|---|---:|---:|---|");
                foreach (var (caso, baseline, candidato) in pioras)
                {
                    var falha = r.AmostrasCandidato.FirstOrDefault(a => a.CasoId == caso.Id && !a.Aprovada)?.Falhas.FirstOrDefault() ?? "—";
                    md.AppendLine($"| {caso.Id} | {caso.Tipo} | {Formato.Percentual(baseline)} | {Formato.Percentual(candidato)} | {Celula(falha)} |");
                }
                md.AppendLine();
            }
        }

        md.AppendLine("### Por tipo de caso");
        md.AppendLine();
        md.AppendLine("| Tipo | Casos | Baseline | Candidato |");
        md.AppendLine("|---|---:|---:|---:|");
        foreach (var grupo in r.Casos.GroupBy(c => c.Tipo).OrderBy(g => DatasetDourado.Tipos.ToList().IndexOf(g.Key)))
        {
            var casos = grupo.ToList();
            var baseline = r.AmostrasBaseline is { } b ? Formato.Percentual(PortaoDeQualidade.TaxaGeral(casos, b)) : "—";
            md.AppendLine($"| {grupo.Key} | {casos.Count} | {baseline} | {Formato.Percentual(PortaoDeQualidade.TaxaGeral(casos, r.AmostrasCandidato))} |");
        }
        md.AppendLine();

        md.AppendLine("### Custo desta execução");
        md.AppendLine();
        md.AppendLine("| Chamadas ao modelo | Servidas do cache | Tokens de entrada | Tokens de saída | Orçamento de tokens |");
        md.AppendLine("|---:|---:|---:|---:|---:|");
        md.AppendLine($"| {Formato.Numero(r.Uso.Chamadas)} | {Formato.Numero(r.Uso.ChamadasDoCache)} | {Formato.Numero(r.Uso.TokensEntrada)} | {Formato.Numero(r.Uso.TokensSaida)} | {Formato.Numero(r.Uso.OrcamentoTokens)} |");
        md.AppendLine();

        AvisoDeRotulos(md, r.Calibracao);

        md.AppendLine("<details><summary>Como o portão decide</summary>");
        md.AppendLine();
        md.AppendLine("1. **Falha crítica:** qualquer amostra reprovada em caso crítico bloqueia, seja qual for a média.");
        md.AppendLine("2. **Distribuição, não número:** cada caso roda N vezes nas duas versões e a diferença das taxas por caso é reamostrada (bootstrap) para obter um intervalo de confiança.");
        md.AppendLine("3. **Piorou** se o intervalo inteiro fica abaixo da margem tolerada; **aprovado** se fica inteiro acima dela; **inconclusivo** se cruza a margem.");
        md.AppendLine("4. **Piso absoluto:** abaixo da taxa mínima bloqueia, mesmo sem piora em relação ao main.");
        md.AppendLine("5. Uma amostra só é aprovada se passar em todos os avaliadores determinísticos e no juiz.");
        md.AppendLine();
        md.AppendLine("</details>");

        return md.ToString();
    }

    public static string GerarMarkdownDaCalibracao(ResultadoCalibracao calibracao, string modeloJuiz)
    {
        var c = calibracao.Concordancia;
        var criterio = calibracao.Criterio;
        var md = new StringBuilder();

        md.AppendLine($"## {(calibracao.Aprovada ? "✅" : "❌")} Calibração do juiz `{modeloJuiz}`: {(calibracao.Aprovada ? "confiável para votar" : "NÃO confiável")}");
        md.AppendLine();
        md.AppendLine($"O juiz julgou {c.Total} respostas que pessoas já tinham rotulado.");
        md.AppendLine();
        md.AppendLine("| Métrica | Obtido | Exigido |");
        md.AppendLine("|---|---:|---:|");
        md.AppendLine($"| Concordância | {Formato.Percentual(c.Taxa)} | ≥ {Formato.Percentual(criterio.ConcordanciaMinima)} |");
        md.AppendLine($"| Kappa de Cohen | {Kappa(calibracao)} | ≥ {criterio.KappaMinimo.ToString("0.00", Formato.PtBr)} |");
        md.AppendLine($"| Falsos aceites em casos críticos | {calibracao.FalsosAceitesCriticos} | ≤ {criterio.FalsosAceitesCriticosMaximos} |");
        md.AppendLine();
        md.AppendLine("| | Juiz aprova | Juiz reprova |");
        md.AppendLine("|---|---:|---:|");
        md.AppendLine($"| **Humano aprova** | {c.AmbosAprovam} | {c.FalsasRejeicoes} (falsa rejeição) |");
        md.AppendLine($"| **Humano reprova** | {c.FalsosAceites} (falso aceite) | {c.AmbosReprovam} |");
        md.AppendLine();

        if (calibracao.Divergencias.Count > 0)
        {
            md.AppendLine("### Divergências");
            md.AppendLine();
            md.AppendLine("| Rótulo | Caso | Humano | Juiz | Motivo humano | Motivo do juiz |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var d in calibracao.Divergencias)
                md.AppendLine($"| {d.RotuloId} | {d.CasoId}{(d.Critico ? " (crítico)" : string.Empty)} | {Voto(d.Humano)} | {Voto(d.Juiz)} | {Celula(d.MotivoHumano)} | {Celula(d.MotivoJuiz ?? "—")} |");
            md.AppendLine();
        }

        AvisoDeRotulos(md, calibracao);
        return md.ToString();
    }

    private static void AvisoDeRotulos(StringBuilder md, ResultadoCalibracao calibracao)
    {
        if (calibracao.RotulosNaoRevisados == 0)
            return;

        md.AppendLine($"> ⚠️ {calibracao.RotulosNaoRevisados} rótulo(s) de calibração sem revisão humana (`revisadoPor` vazio). " +
                      "Até alguém revisar, o juiz está sendo medido contra hipóteses, não contra pessoas.");
        md.AppendLine();
    }

    private static string Icone(Decisao decisao, bool bloqueia) => decisao switch
    {
        Decisao.Aprovado => "✅",
        Decisao.Inconclusivo => bloqueia ? "⛔" : "⚠️",
        _ => "❌",
    };

    private static string Titulo(Decisao decisao) => decisao switch
    {
        Decisao.Aprovado => "aprovado",
        Decisao.Inconclusivo => "inconclusivo",
        Decisao.Piorou => "a mudança piorou o produto",
        Decisao.FalhaCritica => "falha crítica",
        Decisao.AbaixoDoMinimo => "abaixo do mínimo",
        _ => decisao.ToString(),
    };

    private static string Versao(VarianteAvaliada? variante) =>
        variante is null ? "—" : $"`{variante.IA.Versao}` · {variante.IA.Modelo}";

    private static string NotaMedia(IReadOnlyList<ResultadoAmostra>? amostras) =>
        amostras?.Where(a => a.NotaJuiz is not null).Select(a => a.NotaJuiz!.Value).DefaultIfEmpty().Average() is { } media and > 0
            ? media.ToString("0.0", Formato.PtBr)
            : "—";

    private static string Kappa(ResultadoCalibracao calibracao) => calibracao.Concordancia.Kappa.ToString("0.00", Formato.PtBr);

    private static string Voto(bool aprovou) => aprovou ? "aprova" : "reprova";

    private static string Celula(string texto)
    {
        var limpo = texto.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);
        return limpo.Length <= 180 ? limpo : $"{limpo[..177]}...";
    }
}
