using System.Text.Json;
using ConectaSuporte.Avaliacao.Dataset;
using ConectaSuporte.Avaliacao.Portao;
using ConectaSuporte.Avaliacao.Relatorios;

namespace ConectaSuporte.Evals;

/// <summary>
/// Avaliações com modelo real. Ficam em um projeto separado para que "dotnet test" nos testes
/// comuns nunca gaste token por acidente; sem AZURE_OPENAI_ENDPOINT, são puladas.
/// </summary>
public sealed class PortaoDeQualidadeDeIA(ITestOutputHelper saida)
{
    private const string SemModelo =
        "Defina AZURE_OPENAI_ENDPOINT (e AZURE_OPENAI_API_KEY ou faça az login) para rodar as avaliações com modelo real.";

    private static readonly JsonSerializerOptions JsonIndentado = new(JsonDoContrato.Opcoes) { WriteIndented = true };

    [Fact, Trait("etapa", "calibracao")]
    public async Task Juiz_concorda_com_os_rotulos_humanos()
    {
        Assert.SkipUnless(ClienteModelo.Configurado, SemModelo);

        var ambiente = AmbienteDeAvaliacao.Carregar();
        var executor = ambiente.CriarExecutor(ambiente.Portao.Nivel(ambiente.Nivel).OrcamentoTokens);

        var calibracao = await executor.CalibrarJuizAsync(ambiente.Rotulos, ambiente.Casos, ambiente.Candidato.IA, TestContext.Current.CancellationToken);

        Publicar(ambiente, "calibracao.md", RelatorioDoPortao.GerarMarkdownDaCalibracao(calibracao, ambiente.Portao.Juiz.Modelo));
        Assert.True(calibracao.Aprovada, "O juiz não atingiu o critério de calibração: as notas dele não são confiáveis. Veja calibracao.md.");
    }

    [Fact, Trait("etapa", "portao")]
    public async Task Mudanca_nao_piora_o_produto()
    {
        Assert.SkipUnless(ClienteModelo.Configurado, SemModelo);

        var ct = TestContext.Current.CancellationToken;
        var ambiente = AmbienteDeAvaliacao.Carregar();
        var nivel = ambiente.Portao.Nivel(ambiente.Nivel);
        var casos = DatasetDourado.Filtrar(ambiente.Casos, nivel.Casos);
        var executor = ambiente.CriarExecutor(nivel.OrcamentoTokens);

        // 1. Juiz sem calibração não vota. Com cache, recalibrar sem mudanças não custa nada.
        var calibracao = await executor.CalibrarJuizAsync(ambiente.Rotulos, ambiente.Casos, ambiente.Candidato.IA, ct);
        Publicar(ambiente, "calibracao.md", RelatorioDoPortao.GerarMarkdownDaCalibracao(calibracao, ambiente.Portao.Juiz.Modelo));
        Assert.True(calibracao.Aprovada, "O juiz não atingiu o critério de calibração: o portão não pode confiar nele. Veja calibracao.md.");

        // 2. Os mesmos casos, N vezes, nas duas versões do pacote de IA.
        var baseline = ambiente.Baseline is { } variante ? await executor.AvaliarAsync(variante, casos, nivel.AmostrasPorCaso, ct) : null;
        var candidato = await executor.AvaliarAsync(ambiente.Candidato, casos, nivel.AmostrasPorCaso, ct);

        // 3. Decisão objetiva, com o motivo por escrito.
        var regra = ambiente.Portao.Decisao;
        var resultado = PortaoDeQualidade.Decidir(casos, candidato, baseline, regra);
        var bloqueia = resultado.Bloqueia(regra.InconclusivoBloqueia);

        Publicar(ambiente, "relatorio.md", RelatorioDoPortao.GerarMarkdown(new ResumoDaExecucao(
            ambiente.Nivel, nivel.AmostrasPorCaso, casos, ambiente.Candidato, candidato, ambiente.Baseline, baseline,
            resultado, regra, calibracao, ambiente.Portao.Juiz.Modelo, executor.Uso)));

        ambiente.Publicar("resultado.json", JsonSerializer.Serialize(new
        {
            Decisao = resultado.Decisao.ToString(),
            Bloqueia = bloqueia,
            resultado.Motivo,
            resultado.TaxaBaseline,
            resultado.TaxaCandidato,
            resultado.Delta,
            resultado.IcInferior,
            resultado.IcSuperior,
            Uso = new { executor.Uso.Chamadas, executor.Uso.ChamadasReais, executor.Uso.TokensEntrada, executor.Uso.TokensSaida },
            VersaoBaseline = ambiente.Baseline?.IA.Versao,
            VersaoCandidato = ambiente.Candidato.IA.Versao,
            Baseline = baseline,
            Candidato = candidato,
        }, JsonIndentado));

        Assert.False(bloqueia, $"{resultado.Decisao}: {resultado.Motivo} Detalhes em relatorio.md.");
    }

    private void Publicar(AmbienteDeAvaliacao ambiente, string arquivo, string conteudo)
    {
        var caminho = ambiente.Publicar(arquivo, conteudo);
        saida.WriteLine(conteudo);
        saida.WriteLine($"Relatório salvo em {caminho}");
    }
}
