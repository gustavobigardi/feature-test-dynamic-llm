using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ConectaSuporte;

public static class JsonDoContrato
{
    /// <summary>
    /// Mesmas opções para gerar o JSON Schema pedido ao modelo e para ler a resposta.
    /// Campo ausente ou nulo é quebra de contrato, não um valor padrão silencioso.
    /// </summary>
    public static JsonSerializerOptions Opcoes { get; } = Criar();

    private static JsonSerializerOptions Criar()
    {
        var opcoes = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            // Mantém acentos legíveis quando o JSON vira texto para o juiz.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        opcoes.MakeReadOnly();
        return opcoes;
    }
}
