using ConectaSuporte.Avaliacao;
using ConectaSuporte.Avaliacao.Calibracao;
using ConectaSuporte.Avaliacao.Dataset;

namespace ConectaSuporte.Tests;

/// <summary>O dataset dourado também é código: tem teste, revisão e histórico no Git.</summary>
public sealed class DatasetDouradoTests
{
    private static readonly ConfiguracaoIA IA = ConfiguracaoIA.Carregar(Repositorio.Caminho("ia"));
    private static readonly IReadOnlyList<CasoDourado> Casos = DatasetDourado.Carregar(Repositorio.Caminho("evals", "dataset", "casos.jsonl"));
    private static readonly IReadOnlyList<RotuloHumano> Rotulos = Jsonl.Ler<RotuloHumano>(Repositorio.Caminho("evals", "calibracao", "rotulos.jsonl"));

    [Fact]
    public void Dataset_nao_tem_problemas_estruturais() =>
        Assert.Empty(DatasetDourado.Validar(Casos, IA));

    [Fact]
    public void Pelo_menos_um_quarto_dos_casos_e_adversarial()
    {
        var adversariais = Casos.Count(c => c.Tipo == "adversarial");

        Assert.True(adversariais * 4 >= Casos.Count,
            $"Só {adversariais} de {Casos.Count} casos são adversariais. São os casos que todo mundo esquece.");
    }

    [Fact]
    public void Todo_caso_critico_roda_no_smoke() =>
        Assert.All(Casos.Where(c => c.Critico), c => Assert.True(c.Smoke, $"{c.Id} é crítico e precisa rodar em todo PR."));

    [Fact]
    public void Toda_categoria_tem_pelo_menos_um_caso_no_smoke()
    {
        var noSmoke = Casos.Where(c => c.Smoke).Select(c => c.Categoria).ToHashSet();

        Assert.All(Casos.Select(c => c.Categoria).Distinct(), categoria => Assert.Contains(categoria, noSmoke));
    }

    [Fact]
    public void Rotulos_de_calibracao_sao_balanceados_e_apontam_para_casos_existentes()
    {
        var casosPorId = Casos.ToDictionary(c => c.Id);

        Assert.All(Rotulos, r => Assert.True(casosPorId.ContainsKey(r.CasoId), $"{r.Id} aponta para o caso inexistente {r.CasoId}."));
        Assert.InRange(Rotulos.Count(r => r.AprovadoPorHumano) / (double)Rotulos.Count, 0.35, 0.65);

        // Sem respostas ruins em casos críticos, não dá para medir o falso aceite que mais importa.
        Assert.True(Rotulos.Count(r => !r.AprovadoPorHumano && casosPorId[r.CasoId].Critico) >= 5);
    }
}
