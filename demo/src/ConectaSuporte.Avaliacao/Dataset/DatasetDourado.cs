using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConectaSuporte.Avaliacao.Dataset;

public static class Padrao
{
    public static bool Encontra(string texto, string padrao) =>
        Regex.IsMatch(texto, padrao, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}

public static class Jsonl
{
    public static IReadOnlyList<T> Ler<T>(string caminho)
    {
        var itens = new List<T>();
        var numero = 0;
        foreach (var linha in File.ReadLines(caminho))
        {
            numero++;
            if (string.IsNullOrWhiteSpace(linha))
                continue;

            try
            {
                itens.Add(JsonSerializer.Deserialize<T>(linha, JsonDoContrato.Opcoes)
                    ?? throw new JsonException("linha vazia"));
            }
            catch (JsonException erro)
            {
                throw new InvalidDataException($"{Path.GetFileName(caminho)}, linha {numero}: {erro.Message}", erro);
            }
        }

        return itens;
    }
}

public static class DatasetDourado
{
    public static readonly IReadOnlyList<string> Tipos = ["comum", "borda", "adversarial"];

    public static readonly IReadOnlyList<string> Categorias = [.. RespostaSuporte.CategoriasValidas, "seguranca"];

    public static IReadOnlyList<CasoDourado> Carregar(string caminho) => Jsonl.Ler<CasoDourado>(caminho);

    public static IReadOnlyList<CasoDourado> Filtrar(IReadOnlyList<CasoDourado> casos, string filtro) => filtro switch
    {
        "todos" => casos,
        "smoke" => casos.Where(c => c.Smoke).ToList(),
        _ => throw new ArgumentException($"Filtro de casos '{filtro}' desconhecido. Use 'todos' ou 'smoke'.", nameof(filtro)),
    };

    /// <summary>O dataset também é código: quebrado, ele aprova coisa errada em silêncio.</summary>
    public static IReadOnlyList<string> Validar(IReadOnlyList<CasoDourado> casos, ConfiguracaoIA ia)
    {
        var problemas = new List<string>();
        var fontes = ia.BaseDeConhecimento.Select(d => d.Nome).ToHashSet();

        problemas.AddRange(casos.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key}: id duplicado"));

        foreach (var caso in casos)
        {
            if (!Tipos.Contains(caso.Tipo))
                problemas.Add($"{caso.Id}: tipo '{caso.Tipo}' inválido");
            if (!Categorias.Contains(caso.Categoria))
                problemas.Add($"{caso.Id}: categoria '{caso.Categoria}' inválida");
            if (caso.CategoriaEsperada is { } categoria && !RespostaSuporte.CategoriasValidas.Contains(categoria))
                problemas.Add($"{caso.Id}: categoriaEsperada '{categoria}' não existe no contrato");
            if (caso.Deve.Count == 0)
                problemas.Add($"{caso.Id}: sem critérios em 'deve'");
            if (caso.Critico && caso.NaoPode.Count == 0)
                problemas.Add($"{caso.Id}: caso crítico precisa dizer o que a resposta não pode fazer");

            problemas.AddRange(caso.FontesEsperadas.Where(f => !fontes.Contains(f))
                .Select(f => $"{caso.Id}: fonte '{f}' não existe em ia/conhecimento"));

            foreach (var padrao in caso.PadroesObrigatorios.Concat(caso.PadroesProibidos))
            {
                try
                {
                    _ = Padrao.Encontra(string.Empty, padrao);
                }
                catch (ArgumentException)
                {
                    problemas.Add($"{caso.Id}: regex inválida /{padrao}/");
                    return problemas;
                }
            }

            // A própria referência precisa passar nas regras do caso: pega regex mal escrita antes do PR.
            problemas.AddRange(caso.PadroesObrigatorios.Where(p => !Padrao.Encontra(caso.Referencia, p))
                .Select(p => $"{caso.Id}: a referência não contém o padrão obrigatório /{p}/"));
            problemas.AddRange(caso.PadroesProibidos.Where(p => Padrao.Encontra(caso.Referencia, p))
                .Select(p => $"{caso.Id}: a referência contém o padrão proibido /{p}/"));
        }

        return problemas;
    }
}
