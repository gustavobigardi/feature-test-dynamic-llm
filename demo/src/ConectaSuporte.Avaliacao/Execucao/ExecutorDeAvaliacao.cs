using System.Collections.Concurrent;
using System.Text.Json;
using ConectaSuporte.Avaliacao.Avaliadores;
using ConectaSuporte.Avaliacao.Calibracao;
using ConectaSuporte.Avaliacao.Dataset;
using ConectaSuporte.Avaliacao.Portao;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;

namespace ConectaSuporte.Avaliacao.Execucao;

public sealed record VarianteAvaliada(string Nome, ConfiguracaoIA IA);

/// <param name="DiretorioArtefatos">Resultados (para o aieval report) e cache de respostas.</param>
/// <param name="UsarCache">false para detectar drift do modelo: toda chamada vai ao provedor.</param>
public sealed record OpcoesDeExecucao(string DiretorioArtefatos, string NomeExecucao, bool UsarCache, long OrcamentoTokens);

public sealed class ExecutorDeAvaliacao
{
    private readonly ConfiguracaoPortao _portao;
    private readonly OpcoesDeExecucao _opcoes;
    private readonly JuizDeQualidade _juiz;
    private readonly ClientesDeAvaliacao _clientes;

    public ExecutorDeAvaliacao(ConfiguracaoPortao portao, string rubrica, Func<string, IChatClient> criarCliente, OpcoesDeExecucao opcoes)
    {
        _portao = portao;
        _opcoes = opcoes;
        _juiz = new JuizDeQualidade(rubrica, portao.Juiz.NotaMinima);
        Uso = new UsoDoModelo(opcoes.OrcamentoTokens);

        var cache = opcoes.UsarCache
            ? new DiskBasedResponseCacheProvider(opcoes.DiretorioArtefatos, TimeSpan.FromDays(portao.Execucao.ValidadeCacheDias))
            : null;
        _clientes = new ClientesDeAvaliacao(criarCliente, cache, Uso);
    }

    public UsoDoModelo Uso { get; }

    /// <summary>Antes de confiar no juiz, medimos o juiz: ele julga respostas que pessoas já julgaram.</summary>
    public async Task<ResultadoCalibracao> CalibrarJuizAsync(
        IReadOnlyList<RotuloHumano> rotulos, IReadOnlyList<CasoDourado> casos, ConfiguracaoIA ia, CancellationToken cancellationToken)
    {
        var casosPorId = casos.ToDictionary(c => c.Id);
        var juiz = new ChatConfiguration(await _clientes.CriarAsync(_portao.Juiz.Modelo, "juiz", "calibracao", cancellationToken));
        var julgamentos = new ConcurrentBag<(RotuloHumano Rotulo, CasoDourado Caso, bool Aprovou, string? Motivo)>();

        await Parallel.ForEachAsync(rotulos, Paralelismo(cancellationToken), async (rotulo, ct) =>
        {
            var caso = casosPorId.TryGetValue(rotulo.CasoId, out var encontrado)
                ? encontrado
                : throw new InvalidDataException($"{rotulo.Id}: caso '{rotulo.CasoId}' não existe no dataset.");

            var resposta = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(rotulo.Resposta, JsonDoContrato.Opcoes)));
            var resultado = await _juiz.EvaluateAsync(ia.MontarMensagens(caso.Pergunta), resposta, juiz, [new ContextoDoCaso(caso, ia)], ct);
            var aprovado = resultado.Get<BooleanMetric>(JuizDeQualidade.Aprovado);

            julgamentos.Add((rotulo, caso, aprovado.Value == true, aprovado.Reason));
        });

        var lista = julgamentos.OrderBy(j => j.Rotulo.Id, StringComparer.Ordinal).ToList();
        var divergencias = lista
            .Where(j => j.Rotulo.AprovadoPorHumano != j.Aprovou)
            .Select(j => new Divergencia(j.Rotulo.Id, j.Caso.Id, j.Caso.Critico, j.Rotulo.AprovadoPorHumano, j.Aprovou, j.Rotulo.Motivo, j.Motivo))
            .ToList();

        return new ResultadoCalibracao(
            Concordancia.Calcular([.. lista.Select(j => (j.Rotulo.AprovadoPorHumano, j.Aprovou))]),
            divergencias,
            FalsosAceitesCriticos: divergencias.Count(d => d.Critico && !d.Humano && d.Juiz),
            RotulosNaoRevisados: lista.Count(j => string.IsNullOrWhiteSpace(j.Rotulo.RevisadoPor)),
            _portao.Calibracao);
    }

    /// <summary>Gera N respostas por caso para uma versão do pacote de IA e avalia cada uma.</summary>
    public async Task<IReadOnlyList<ResultadoAmostra>> AvaliarAsync(
        VarianteAvaliada variante, IReadOnlyList<CasoDourado> casos, int amostrasPorCaso, CancellationToken cancellationToken)
    {
        var reporting = new ReportingConfiguration(
            evaluators: [new AvaliadorDeContrato(), new AvaliadorDeRegrasDoCaso(), new AvaliadorDeSeguranca(), _juiz],
            resultStore: new DiskBasedResultStore(_opcoes.DiretorioArtefatos),
            chatConfiguration: new ChatConfiguration(await _clientes.CriarAsync(_portao.Juiz.Modelo, "juiz", "produto", cancellationToken)),
            executionName: $"{_opcoes.NomeExecucao}-{variante.Nome}");

        var trabalhos = casos.SelectMany(caso => Enumerable.Range(1, amostrasPorCaso).Select(amostra => (caso, amostra)));
        var resultados = new ConcurrentBag<ResultadoAmostra>();

        await Parallel.ForEachAsync(trabalhos, Paralelismo(cancellationToken), async (trabalho, ct) =>
        {
            var (caso, amostra) = trabalho;

            // Cada amostra tem sua entrada de cache: repetir a chamada gera uma nova resposta, não a mesma.
            var gerador = await _clientes.CriarAsync(variante.IA.Modelo, $"geracao.{caso.Id}", $"amostra-{amostra}", ct);
            var resposta = await new AssistenteSuporte(gerador, variante.IA).ResponderAsync(caso.Pergunta, ct);

            await using var cenario = await reporting.CreateScenarioRunAsync(
                $"{caso.Categoria}.{caso.Id}", $"{variante.Nome}-{amostra}",
                additionalTags: [variante.Nome, caso.Tipo, caso.Critico ? "critico" : "nao-critico"],
                cancellationToken: ct);

            var avaliacao = await cenario.EvaluateAsync(resposta.Mensagens, resposta.Bruta, [new ContextoDoCaso(caso, variante.IA)], ct);
            resultados.Add(Consolidar(caso, variante.Nome, amostra, avaliacao, resposta));
        });

        return [.. resultados.OrderBy(r => r.CasoId, StringComparer.Ordinal).ThenBy(r => r.Amostra)];
    }

    /// <summary>A amostra só é aprovada se nenhuma métrica, determinística ou do juiz, falhar.</summary>
    private static ResultadoAmostra Consolidar(CasoDourado caso, string variante, int amostra, EvaluationResult avaliacao, RespostaDoAssistente resposta)
    {
        var falhas = avaliacao.Metrics.Values
            .Where(m => m.Interpretation?.Failed == true)
            .Select(m => $"{m.Name}: {m.Reason}")
            .ToList();

        var nota = avaliacao.Metrics.TryGetValue(JuizDeQualidade.Nota, out var metrica) ? (metrica as NumericMetric)?.Value : null;

        return new ResultadoAmostra(caso.Id, variante, amostra, falhas.Count == 0, nota, falhas,
            resposta.Estruturada?.Resposta ?? resposta.Bruta.Text);
    }

    private ParallelOptions Paralelismo(CancellationToken cancellationToken) =>
        new() { MaxDegreeOfParallelism = _portao.Execucao.Paralelismo, CancellationToken = cancellationToken };
}
