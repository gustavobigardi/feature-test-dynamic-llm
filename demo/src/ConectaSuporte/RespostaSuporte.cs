using System.ComponentModel;

namespace ConectaSuporte;

/// <summary>Contrato da feature: o que a API devolve e o que os avaliadores recebem.</summary>
public sealed record RespostaSuporte(
    [property: Description("Texto exibido ao cliente.")] string Resposta,
    [property: Description("planos, fatura, cancelamento, suporte-tecnico, instabilidade ou fora-do-escopo.")] string Categoria,
    [property: Description("true quando o cliente precisa de atendimento humano para concluir o pedido.")] bool EncaminharParaHumano,
    [property: Description("Arquivos da base de conhecimento usados na resposta.")] IReadOnlyList<string> Fontes)
{
    public static readonly IReadOnlyList<string> CategoriasValidas =
        ["planos", "fatura", "cancelamento", "suporte-tecnico", "instabilidade", "fora-do-escopo"];
}
