using System.Globalization;
using ConectaSuporte.Avaliacao;
using ConectaSuporte.Avaliacao.Calibracao;
using ConectaSuporte.Avaliacao.Dataset;
using ConectaSuporte.Avaliacao.Execucao;
using ConectaSuporte.Avaliacao.Portao;

namespace ConectaSuporte.Evals;

/// <summary>
/// Variáveis de ambiente das avaliações:
/// EVAL_NIVEL (smoke | completo), EVAL_BASELINE_DIR (checkout do main), EVAL_ARTEFATOS,
/// EVAL_EXECUCAO (nome da execução) e EVAL_SEM_CACHE=true (para detectar drift do modelo).
/// </summary>
internal sealed record AmbienteDeAvaliacao(
    string Nivel,
    ConfiguracaoPortao Portao,
    string Rubrica,
    IReadOnlyList<CasoDourado> Casos,
    IReadOnlyList<RotuloHumano> Rotulos,
    VarianteAvaliada Candidato,
    VarianteAvaliada? Baseline,
    string DiretorioArtefatos,
    string NomeExecucao,
    bool UsarCache)
{
    public static AmbienteDeAvaliacao Carregar()
    {
        var diretorioBaseline = Variavel("EVAL_BASELINE_DIR");

        return new AmbienteDeAvaliacao(
            Nivel: Variavel("EVAL_NIVEL") ?? "smoke",
            Portao: ConfiguracaoPortao.Carregar(Repositorio.Caminho("evals", "portao.json")),
            Rubrica: File.ReadAllText(Repositorio.Caminho("evals", "juiz", "rubrica.md")),
            Casos: DatasetDourado.Carregar(Repositorio.Caminho("evals", "dataset", "casos.jsonl")),
            Rotulos: Jsonl.Ler<RotuloHumano>(Repositorio.Caminho("evals", "calibracao", "rotulos.jsonl")),
            Candidato: new VarianteAvaliada("candidato", ConfiguracaoIA.Carregar(Repositorio.Caminho("ia"))),
            Baseline: diretorioBaseline is null ? null : new VarianteAvaliada("baseline", ConfiguracaoIA.Carregar(Path.Combine(diretorioBaseline, "ia"))),
            DiretorioArtefatos: Variavel("EVAL_ARTEFATOS") ?? Repositorio.Caminho("artifacts", "evals"),
            NomeExecucao: Variavel("EVAL_EXECUCAO") ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
            UsarCache: !string.Equals(Variavel("EVAL_SEM_CACHE"), "true", StringComparison.OrdinalIgnoreCase));
    }

    public ExecutorDeAvaliacao CriarExecutor(long orcamentoTokens) =>
        new(Portao, Rubrica, ClienteModelo.Criar, new OpcoesDeExecucao(DiretorioArtefatos, NomeExecucao, UsarCache, orcamentoTokens));

    public string Publicar(string arquivo, string conteudo)
    {
        Directory.CreateDirectory(DiretorioArtefatos);
        var caminho = Path.Combine(DiretorioArtefatos, arquivo);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    private static string? Variavel(string nome) =>
        Environment.GetEnvironmentVariable(nome) is { Length: > 0 } valor ? valor : null;
}
