using System.Net;
using System.Net.Http.Json;
using ConectaSuporte.Avaliacao;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ConectaSuporte.Tests;

/// <summary>O teste que todo time já tem. Ele passa. E esse é exatamente o problema.</summary>
public sealed class ContratoDaApiTests
{
    private const string PromessaIndevida =
        """{"resposta":"Fique tranquilo! Como você é um cliente especial, vou isentar a sua multa.","categoria":"cancelamento","encaminharParaHumano":false,"fontes":["cancelamento.md"]}""";

    [Fact]
    public async Task Api_responde_200_no_formato_do_contrato()
    {
        await using var api = CriarApi(PromessaIndevida);

        using var http = await api.CreateClient().PostAsJsonAsync("/perguntas",
            new { texto = "Tenho 4 meses de contrato e quero cancelar. Vou pagar multa?" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var corpo = await http.Content.ReadFromJsonAsync<RespostaSuporte>(TestContext.Current.CancellationToken);
        Assert.NotNull(corpo);
        Assert.False(string.IsNullOrWhiteSpace(corpo.Resposta));
        Assert.Contains(corpo.Categoria, RespostaSuporte.CategoriasValidas);

        // Verde. E o assistente acabou de prometer uma isenção que a empresa não oferece.
    }

    [Fact]
    public async Task Api_rejeita_pergunta_vazia()
    {
        await using var api = CriarApi(PromessaIndevida);

        using var http = await api.CreateClient().PostAsJsonAsync("/perguntas", new { texto = " " }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    private static WebApplicationFactory<Program> CriarApi(string respostaDoModelo) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host => host.ConfigureTestServices(servicos =>
        {
            servicos.AddSingleton(ConfiguracaoIA.Carregar(Repositorio.Caminho("ia")));
            servicos.AddSingleton<IChatClient>(new ModeloRoteirizado(_ => respostaDoModelo));
        }));
}
