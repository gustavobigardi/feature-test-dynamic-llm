namespace ConectaSuporte.Avaliacao.Dataset;

/// <summary>
/// Um caso do dataset dourado. Não guarda "a" resposta certa: guarda o que qualquer resposta aceitável
/// precisa fazer. O que dá para checar com código vira padrão; o resto vira critério para o juiz.
/// </summary>
public sealed record CasoDourado
{
    public required string Id { get; init; }

    /// <summary>Área do produto, para agrupar resultados.</summary>
    public required string Categoria { get; init; }

    /// <summary>comum, borda ou adversarial.</summary>
    public required string Tipo { get; init; }

    /// <summary>Uma única amostra reprovada bloqueia o merge, independentemente da média.</summary>
    public bool Critico { get; init; }

    /// <summary>Entra no nível smoke, que roda em todo PR que mexe em IA.</summary>
    public bool Smoke { get; init; }

    public required string Pergunta { get; init; }

    /// <summary>Uma resposta aceitável, usada pelo juiz como referência (não como gabarito literal).</summary>
    public required string Referencia { get; init; }

    /// <summary>Critérios semânticos para o juiz.</summary>
    public IReadOnlyList<string> Deve { get; init; } = [];

    /// <summary>Critérios semânticos para o juiz.</summary>
    public IReadOnlyList<string> NaoPode { get; init; } = [];

    /// <summary>Regex que precisam aparecer na resposta (valores, prazos). Checagem determinística.</summary>
    public IReadOnlyList<string> PadroesObrigatorios { get; init; } = [];

    /// <summary>Regex que não podem aparecer. Checagem determinística; paráfrases escapam, por isso existe o juiz.</summary>
    public IReadOnlyList<string> PadroesProibidos { get; init; } = [];

    public string? CategoriaEsperada { get; init; }

    public bool? EncaminharParaHumano { get; init; }

    public IReadOnlyList<string> FontesEsperadas { get; init; } = [];
}
