using ConectaSuporte.Avaliacao.Calibracao;
using ConectaSuporte.Avaliacao.Dataset;
using ConectaSuporte.Avaliacao.Portao;

namespace ConectaSuporte.Tests;

public sealed class PortaoDeQualidadeTests
{
    private static readonly RegraDeDecisao Regra = new(TaxaMinima: 0.8, Margem: 0.05, Confianca: 0.95, Reamostras: 10_000, Semente: 2026, InconclusivoBloqueia: true);

    [Fact]
    public void Candidato_igual_ao_baseline_e_aprovado()
    {
        var casos = Casos(30);

        var resultado = PortaoDeQualidade.Decidir(casos, Amostras(casos, (_, _) => true), Amostras(casos, (_, _) => true), Regra);

        Assert.Equal(Decisao.Aprovado, resultado.Decisao);
        Assert.Equal(0d, resultado.Delta);
    }

    [Fact]
    public void Queda_grande_e_consistente_piorou()
    {
        var casos = Casos(30);
        var falham = casos.Take(8).Select(c => c.Id).ToHashSet();

        var resultado = PortaoDeQualidade.Decidir(casos, Amostras(casos, (id, _) => !falham.Contains(id)), Amostras(casos, (_, _) => true), Regra);

        Assert.Equal(Decisao.Piorou, resultado.Decisao);
        Assert.True(resultado.IcSuperior < -Regra.Margem);
    }

    [Fact]
    public void Queda_pequena_com_pouca_amostra_e_inconclusiva()
    {
        var casos = Casos(30);
        var falham = casos.Take(2).Select(c => c.Id).ToHashSet();

        var resultado = PortaoDeQualidade.Decidir(casos, Amostras(casos, (id, _) => !falham.Contains(id)), Amostras(casos, (_, _) => true), Regra);

        Assert.Equal(Decisao.Inconclusivo, resultado.Decisao);
        Assert.True(resultado.Bloqueia(inconclusivoBloqueia: true));
        Assert.False(resultado.Bloqueia(inconclusivoBloqueia: false));
    }

    [Fact]
    public void Uma_amostra_reprovada_em_caso_critico_bloqueia_mesmo_com_media_alta()
    {
        var casos = Casos(30, criticos: 3);

        var resultado = PortaoDeQualidade.Decidir(casos, Amostras(casos, (id, amostra) => !(id == "C03" && amostra == 2)), Amostras(casos, (_, _) => true), Regra);

        Assert.Equal(Decisao.FalhaCritica, resultado.Decisao);
        Assert.Single(resultado.FalhasCriticas);
        Assert.True(resultado.TaxaCandidato > 0.95);
    }

    [Fact]
    public void Candidato_tao_ruim_quanto_o_baseline_fica_abaixo_do_minimo()
    {
        var casos = Casos(30);
        var falham = casos.Take(9).Select(c => c.Id).ToHashSet();

        var resultado = PortaoDeQualidade.Decidir(casos, Amostras(casos, (id, _) => !falham.Contains(id)), Amostras(casos, (id, _) => !falham.Contains(id)), Regra);

        Assert.Equal(Decisao.AbaixoDoMinimo, resultado.Decisao);
    }

    [Fact]
    public void Sem_baseline_valem_so_os_criterios_absolutos()
    {
        var casos = Casos(10);

        var resultado = PortaoDeQualidade.Decidir(casos, Amostras(casos, (_, _) => true), baseline: null, Regra);

        Assert.Equal(Decisao.Aprovado, resultado.Decisao);
        Assert.Null(resultado.IcInferior);
    }

    [Fact]
    public void Bootstrap_e_reprodutivel_com_a_mesma_semente()
    {
        double[] deltas = [0, 0, -1, 0, 0.5, 0, -0.5, 0, 0, 0];

        var primeiro = Bootstrap.IntervaloDaMedia(deltas, 5_000, 0.95, semente: 7);
        var segundo = Bootstrap.IntervaloDaMedia(deltas, 5_000, 0.95, semente: 7);

        Assert.Equal(primeiro, segundo);
        Assert.True(primeiro.Inferior < deltas.Average() && deltas.Average() < primeiro.Superior);
    }

    [Fact]
    public void Kappa_desconta_a_concordancia_por_acaso()
    {
        (bool, bool)[] pares =
        [
            (true, true), (true, true), (true, true), (true, true), (true, false),
            (false, false), (false, false), (false, false), (false, false), (false, true),
        ];

        var concordancia = Concordancia.Calcular(pares);

        Assert.Equal(0.8, concordancia.Taxa, 3);
        Assert.Equal(0.6, concordancia.Kappa, 3);
        Assert.Equal(1, concordancia.FalsosAceites);
        Assert.Equal(1, concordancia.FalsasRejeicoes);
    }

    [Fact]
    public void Juiz_que_aprova_tudo_tem_kappa_zero_mesmo_com_concordancia_alta()
    {
        // 9 de 10 respostas eram boas: aprovar tudo acerta 90% e não sabe distinguir nada.
        var pares = Enumerable.Range(0, 10).Select(i => (Humano: i != 0, Juiz: true)).ToList();

        var concordancia = Concordancia.Calcular(pares);

        Assert.Equal(0.9, concordancia.Taxa, 3);
        Assert.Equal(0d, concordancia.Kappa, 3);
    }

    private static List<CasoDourado> Casos(int quantidade, params int[] criticos) =>
    [
        .. Enumerable.Range(1, quantidade).Select(i => new CasoDourado
        {
            Id = $"C{i:00}",
            Categoria = "planos",
            Tipo = "comum",
            Critico = criticos.Contains(i),
            Pergunta = $"Pergunta {i}",
            Referencia = "Resposta.",
        }),
    ];

    private static List<ResultadoAmostra> Amostras(IEnumerable<CasoDourado> casos, Func<string, int, bool> aprovada, int porCaso = 3) =>
    [
        .. casos.SelectMany(c => Enumerable.Range(1, porCaso)
            .Select(a => new ResultadoAmostra(c.Id, "teste", a, aprovada(c.Id, a), null, [], string.Empty))),
    ];
}
