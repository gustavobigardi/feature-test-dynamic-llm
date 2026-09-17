using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ConectaSuporte;

public sealed record Documento(string Nome, string Conteudo);

/// <summary>
/// Tudo o que muda o comportamento da IA, versionado no Git: modelo, prompt e base de conhecimento.
/// O portão de qualidade carrega este pacote duas vezes: do main (baseline) e do PR (candidato).
/// </summary>
public sealed record ConfiguracaoIA
{
    public required string Modelo { get; init; }

    public int MaxTokensSaida { get; init; } = 4000;

    public required string Prompt { get; init; }

    public required IReadOnlyList<Documento> BaseDeConhecimento { get; init; }

    /// <summary>Hash curto do pacote, para o relatório dizer exatamente o que foi avaliado.</summary>
    public string Versao
    {
        get
        {
            var conteudo = new StringBuilder().Append(Modelo).Append('\n').Append(MaxTokensSaida).Append('\n').Append(Prompt);
            foreach (var documento in BaseDeConhecimento)
                conteudo.Append('\n').Append(documento.Nome).Append('\n').Append(documento.Conteudo);

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo.ToString())))[..7];
        }
    }

    public static ConfiguracaoIA Carregar(string diretorio)
    {
        var arquivo = JsonSerializer.Deserialize<ArquivoAssistente>(
            File.ReadAllText(Path.Combine(diretorio, "assistente.json")), JsonDoContrato.Opcoes)
            ?? throw new InvalidDataException($"assistente.json inválido em {diretorio}.");

        var documentos = Directory.GetFiles(Path.Combine(diretorio, "conhecimento"), "*.md")
            .Order(StringComparer.Ordinal)
            .Select(caminho => new Documento(Path.GetFileName(caminho), Normalizar(File.ReadAllText(caminho))))
            .ToList();

        return new ConfiguracaoIA
        {
            Modelo = arquivo.Modelo,
            MaxTokensSaida = arquivo.MaxTokensSaida ?? 4000,
            Prompt = Normalizar(File.ReadAllText(Path.Combine(diretorio, "prompt.md"))),
            BaseDeConhecimento = documentos,
        };
    }

    public string BaseComoTexto() =>
        string.Join("\n\n", BaseDeConhecimento.Select(d => $"## Fonte: {d.Nome}\n\n{d.Conteudo}"));

    public List<ChatMessage> MontarMensagens(string pergunta) =>
    [
        new(ChatRole.System, $"{Prompt}\n\n# Base de conhecimento\n\n{BaseComoTexto()}"),
        new(ChatRole.User, pergunta),
    ];

    public ChatOptions CriarOpcoes() => new() { ModelId = Modelo, MaxOutputTokens = MaxTokensSaida };

    private static string Normalizar(string texto) => texto.ReplaceLineEndings("\n").Trim();

    private sealed record ArquivoAssistente(string Modelo, int? MaxTokensSaida);
}
