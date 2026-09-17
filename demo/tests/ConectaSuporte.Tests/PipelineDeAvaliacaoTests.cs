using System.Text.Json;
using ConectaSuporte.Avaliacao;
using ConectaSuporte.Avaliacao.Dataset;
using ConectaSuporte.Avaliacao.Execucao;
using ConectaSuporte.Avaliacao.Portao;

namespace ConectaSuporte.Tests;

/// <summary>
/// O portão inteiro (geração, avaliadores, juiz, cache, orçamento e decisão) com modelos falsos.
/// Prova que a engrenagem funciona sem gastar um token; o comportamento do modelo real é assunto dos evals.
/// </summary>
public sealed class PipelineDeAvaliacaoTests : IDisposable
{
    private static readonly ConfiguracaoIA Main = ConfiguracaoIA.Carregar(Repositorio.Caminho("ia"));
    private static readonly ConfiguracaoIA TresPalavras = Main with
    {
        Prompt = Main.Prompt.Replace("Seja transparente sobre custos", "Seja gentil sobre custos", StringComparison.Ordinal),
    };
    private static readonly ConfiguracaoPortao Portao = ConfiguracaoPortao.Carregar(Repositorio.Caminho("evals", "portao.json"));
    private static readonly IReadOnlyList<CasoDourado> Smoke =
        DatasetDourado.Filtrar(DatasetDourado.Carregar(Repositorio.Caminho("evals", "dataset", "casos.jsonl")), "smoke");

    private readonly string _artefatos = Path.Combine(Path.GetTempPath(), $"conecta-evals-{Guid.NewGuid():N}");

    [Fact]
    public async Task Pega_a_piora_e_a_segunda_execucao_sai_toda_do_cache()
    {
        var ct = TestContext.Current.CancellationToken;

        var primeira = CriarExecutor(orcamentoTokens: 10_000_000);
        var baseline = await primeira.AvaliarAsync(new VarianteAvaliada("baseline", Main), Smoke, 2, ct);
        var candidato = await primeira.AvaliarAsync(new VarianteAvaliada("candidato", TresPalavras), Smoke, 2, ct);
        var resultado = PortaoDeQualidade.Decidir(Smoke, candidato, baseline, Portao.Decisao);

        Assert.Equal(1d, PortaoDeQualidade.TaxaGeral(Smoke, baseline));
        Assert.Equal(Decisao.FalhaCritica, resultado.Decisao);
        Assert.Contains(resultado.FalhasCriticas, f => f.CasoId == "CAN-01");
        Assert.True(primeira.Uso.ChamadasReais > 0);

        // Nada mudou desde a primeira execução: nenhuma chamada chega ao provedor.
        var segunda = CriarExecutor(orcamentoTokens: 10_000_000);
        await segunda.AvaliarAsync(new VarianteAvaliada("baseline", Main), Smoke, 2, ct);
        await segunda.AvaliarAsync(new VarianteAvaliada("candidato", TresPalavras), Smoke, 2, ct);

        Assert.Equal(0, segunda.Uso.ChamadasReais);
        Assert.Equal(primeira.Uso.Chamadas, segunda.Uso.ChamadasDoCache);
    }

    [Fact]
    public async Task Orcamento_esgotado_interrompe_em_vez_de_aprovar()
    {
        var executor = CriarExecutor(orcamentoTokens: 1);

        await Assert.ThrowsAsync<OrcamentoEsgotadoException>(() =>
            executor.AvaliarAsync(new VarianteAvaliada("candidato", Main), Smoke, 2, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_artefatos))
            Directory.Delete(_artefatos, recursive: true);
    }

    private ExecutorDeAvaliacao CriarExecutor(long orcamentoTokens) => new(
        Portao,
        rubrica: "Rubrica de teste.",
        criarCliente: modelo => modelo == Portao.Juiz.Modelo ? JuizFalso() : GeradorFalso(),
        new OpcoesDeExecucao(_artefatos, "teste", UsarCache: true, orcamentoTokens));

    /// <summary>Responde a referência do caso; com as "três palavras", passa a amaciar a multa.</summary>
    private static ModeloRoteirizado GeradorFalso() => new(mensagens =>
    {
        var caso = Smoke.Single(c => c.Pergunta == mensagens[^1].Text);
        var gentil = mensagens[0].Text.Contains("gentil sobre custos", StringComparison.Ordinal);

        var texto = gentil && caso.Categoria == "cancelamento" ? "Fique tranquilo, vou isentar a sua multa." : caso.Referencia;
        var categoria = caso.CategoriaEsperada
            ?? (RespostaSuporte.CategoriasValidas.Contains(caso.Categoria) ? caso.Categoria : "fora-do-escopo");

        return JsonSerializer.Serialize(
            new RespostaSuporte(texto, categoria, caso.EncaminharParaHumano ?? false, caso.FontesEsperadas), JsonDoContrato.Opcoes);
    });

    /// <summary>Reprova promessa de isenção na resposta avaliada ("Não consigo isentar" é recusa correta); aprova o resto.</summary>
    private static ModeloRoteirizado JuizFalso() => new(mensagens =>
    {
        var entrada = mensagens[^1].Text;
        var avaliada = entrada[entrada.IndexOf("<resposta_avaliada>", StringComparison.Ordinal)..];

        return avaliada.Contains("vou isentar", StringComparison.OrdinalIgnoreCase)
            ? """{"nota":1,"violacoes":["Promete isenção que a empresa não oferece."],"justificativa":"Promessa indevida."}"""
            : """{"nota":5,"violacoes":[],"justificativa":"Correta e fundamentada."}""";
    });
}
