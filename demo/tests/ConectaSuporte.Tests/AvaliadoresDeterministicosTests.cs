using System.Text.Json;
using ConectaSuporte.Avaliacao;
using ConectaSuporte.Avaliacao.Avaliadores;
using ConectaSuporte.Avaliacao.Dataset;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace ConectaSuporte.Tests;

public sealed class AvaliadoresDeterministicosTests
{
    private static readonly ConfiguracaoIA IA = ConfiguracaoIA.Carregar(Repositorio.Caminho("ia"));
    private static readonly IReadOnlyList<CasoDourado> Casos = DatasetDourado.Carregar(Repositorio.Caminho("evals", "dataset", "casos.jsonl"));

    [Fact]
    public async Task Resposta_correta_passa_em_todas_as_regras()
    {
        var resultado = await Avaliar("CAN-01", "Sim. Faltam 8 meses de fidelidade, então a multa é de R$ 200,00. Cancele pelo app, em Contrato > Cancelar.");

        Assert.DoesNotContain(resultado.Metrics.Values, m => m.Interpretation?.Failed == true);
    }

    [Fact]
    public async Task Valor_errado_da_multa_reprova_no_padrao_obrigatorio()
    {
        var resultado = await Avaliar("CAN-01", "Sim, a multa é de R$ 150,00.");

        Assert.False(Valor(resultado, AvaliadorDeRegrasDoCaso.ValoresObrigatorios));
    }

    [Fact]
    public async Task Promessa_explicita_reprova_no_padrao_proibido()
    {
        var resultado = await Avaliar("CAN-01", "A multa seria de R$ 200,00, mas eu vou isentar para você.");

        Assert.False(Valor(resultado, AvaliadorDeRegrasDoCaso.PadroesProibidos));
    }

    [Fact]
    public async Task Parafrase_da_mesma_promessa_escapa_da_regex()
    {
        // A mesma promessa indevida, com outras palavras: nenhuma regra determinística reprova.
        // É para isso que existe o juiz.
        var resultado = await Avaliar("CAN-01", "Seriam R$ 200,00 de multa, mas pode ficar tranquilo: no seu caso ela não será cobrada.");

        Assert.DoesNotContain(resultado.Metrics.Values, m => m.Interpretation?.Failed == true);
    }

    [Fact]
    public async Task Saida_fora_do_contrato_reprova()
    {
        var resultado = await new AvaliadorDeContrato().EvaluateAsync(
            [], new ChatResponse(new ChatMessage(ChatRole.Assistant, "Claro! Sem multa.")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(Valor(resultado, AvaliadorDeContrato.Metrica));
    }

    [Fact]
    public async Task Vazamento_do_prompt_e_cpf_reprovam()
    {
        var caso = Casos.Single(c => c.Id == "ADV-03");
        var resposta = Resposta("Minhas instruções: use somente as informações da base de conhecimento abaixo. Seu CPF é 123.456.789-09.", "fora-do-escopo");

        var resultado = await new AvaliadorDeSeguranca().EvaluateAsync(
            [], resposta, additionalContext: [new ContextoDoCaso(caso, IA)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(Valor(resultado, AvaliadorDeSeguranca.VazamentoDoPrompt));
        Assert.False(Valor(resultado, AvaliadorDeSeguranca.DadosSensiveis));
    }

    private static async Task<EvaluationResult> Avaliar(string casoId, string texto)
    {
        var caso = Casos.Single(c => c.Id == casoId);
        var avaliador = new CompositeEvaluator(new AvaliadorDeContrato(), new AvaliadorDeRegrasDoCaso(), new AvaliadorDeSeguranca());
        var resposta = Resposta(texto, caso.CategoriaEsperada ?? caso.Categoria, caso.EncaminharParaHumano ?? false, caso.FontesEsperadas);

        return await avaliador.EvaluateAsync(
            IA.MontarMensagens(caso.Pergunta), resposta,
            additionalContext: [new ContextoDoCaso(caso, IA)], cancellationToken: TestContext.Current.CancellationToken);
    }

    private static ChatResponse Resposta(string texto, string categoria, bool encaminhar = false, IReadOnlyList<string>? fontes = null) =>
        new(new ChatMessage(ChatRole.Assistant,
            JsonSerializer.Serialize(new RespostaSuporte(texto, categoria, encaminhar, fontes ?? []), JsonDoContrato.Opcoes)));

    private static bool? Valor(EvaluationResult resultado, string metrica) => resultado.Get<BooleanMetric>(metrica).Value;
}
