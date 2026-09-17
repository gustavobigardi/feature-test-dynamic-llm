using System.Text.Json;

namespace ConectaSuporte.Avaliacao.Portao;

/// <summary>evals/portao.json: as regras do jogo, versionadas e revisadas como código.</summary>
public sealed record ConfiguracaoPortao(
    ConfiguracaoJuiz Juiz,
    CriterioCalibracao Calibracao,
    IReadOnlyDictionary<string, NivelDeAvaliacao> Niveis,
    RegraDeDecisao Decisao,
    ConfiguracaoExecucao Execucao)
{
    public static ConfiguracaoPortao Carregar(string caminho) =>
        JsonSerializer.Deserialize<ConfiguracaoPortao>(File.ReadAllText(caminho), JsonDoContrato.Opcoes)
        ?? throw new InvalidDataException($"{caminho} inválido.");

    public NivelDeAvaliacao Nivel(string nome) =>
        Niveis.TryGetValue(nome, out var nivel)
            ? nivel
            : throw new ArgumentException($"Nível '{nome}' não existe em portao.json. Use: {string.Join(", ", Niveis.Keys)}.", nameof(nome));
}

public sealed record ConfiguracaoJuiz(string Modelo, int NotaMinima);

public sealed record CriterioCalibracao(double ConcordanciaMinima, double KappaMinimo, int FalsosAceitesCriticosMaximos);

/// <param name="Casos">"smoke" ou "todos".</param>
public sealed record NivelDeAvaliacao(string Casos, int AmostrasPorCaso, long OrcamentoTokens);

/// <param name="TaxaMinima">Piso absoluto: comparar com o main não basta se os dois forem ruins.</param>
/// <param name="Margem">Perda tolerada na taxa de aprovação (0,05 = 5 pontos percentuais).</param>
public sealed record RegraDeDecisao(double TaxaMinima, double Margem, double Confianca, int Reamostras, int Semente, bool InconclusivoBloqueia);

public sealed record ConfiguracaoExecucao(int Paralelismo, int ValidadeCacheDias);
