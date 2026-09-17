using System.Collections.Concurrent;
using ConectaSuporte.Avaliacao.Portao;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace ConectaSuporte.Avaliacao.Execucao;

/// <summary>Quanto a execução custou de verdade: chamadas que foram ao provedor e tokens gastos.</summary>
public sealed class UsoDoModelo(long orcamentoTokens)
{
    private long _chamadas;
    private long _chamadasReais;
    private long _tokensEntrada;
    private long _tokensSaida;

    public long OrcamentoTokens => orcamentoTokens;

    public long Chamadas => Interlocked.Read(ref _chamadas);

    public long ChamadasReais => Interlocked.Read(ref _chamadasReais);

    public long ChamadasDoCache => Chamadas - ChamadasReais;

    public long TokensEntrada => Interlocked.Read(ref _tokensEntrada);

    public long TokensSaida => Interlocked.Read(ref _tokensSaida);

    public long Tokens => TokensEntrada + TokensSaida;

    internal void RegistrarChamada() => Interlocked.Increment(ref _chamadas);

    internal void GarantirOrcamento()
    {
        if (Tokens >= orcamentoTokens)
            throw new OrcamentoEsgotadoException(Tokens, orcamentoTokens);
    }

    internal void RegistrarChamadaReal(UsageDetails? uso)
    {
        Interlocked.Increment(ref _chamadasReais);
        Interlocked.Add(ref _tokensEntrada, uso?.InputTokenCount ?? 0);
        Interlocked.Add(ref _tokensSaida, uso?.OutputTokenCount ?? 0);
    }
}

public sealed class OrcamentoEsgotadoException(long gastos, long orcamento)
    : Exception($"Orçamento de tokens esgotado: {Formato.Numero(gastos)} de {Formato.Numero(orcamento)}. Execução incompleta não é aprovação.");

/// <summary>
/// Pipeline de cada IChatClient da avaliação:
/// [conta chamada] → [cache em disco] → [orçamento + conta tokens reais] → provedor.
/// Resposta servida do cache não passa pelo orçamento: não custou nada.
/// </summary>
internal sealed class ClientesDeAvaliacao(Func<string, IChatClient> criarCliente, IEvaluationResponseCacheProvider? cache, UsoDoModelo uso)
{
    private readonly ConcurrentDictionary<string, IChatClient> _provedores = new();

    public async Task<IChatClient> CriarAsync(string modelo, string cenario, string iteracao, CancellationToken cancellationToken)
    {
        var builder = new ChatClientBuilder(_provedores.GetOrAdd(modelo, criarCliente))
            .Use(async (mensagens, opcoes, proximo, ct) =>
            {
                uso.RegistrarChamada();
                return await proximo.GetResponseAsync(mensagens, opcoes, ct);
            }, getStreamingResponseFunc: null);

        if (cache is not null)
        {
            var armazenamento = await cache.GetCacheAsync(cenario, iteracao, cancellationToken);

            // A chave já inclui mensagens e opções; o modelo entra explicitamente: trocou o modelo, o cache não vale.
            builder.UseDistributedCache(armazenamento, c => c.CacheKeyAdditionalValues = [modelo]);
        }

        return builder
            .Use(async (mensagens, opcoes, proximo, ct) =>
            {
                uso.GarantirOrcamento();
                var resposta = await proximo.GetResponseAsync(mensagens, opcoes, ct);
                uso.RegistrarChamadaReal(resposta.Usage);
                return resposta;
            }, getStreamingResponseFunc: null)
            .Build();
    }
}
