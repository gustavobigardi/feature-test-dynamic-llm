using ConectaSuporte;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp => ConfiguracaoIA.Carregar(
    sp.GetRequiredService<IConfiguration>()["IA:Diretorio"] ?? Path.Combine(AppContext.BaseDirectory, "ia")));
builder.Services.AddSingleton<IChatClient>(sp => ClienteModelo.Criar(sp.GetRequiredService<ConfiguracaoIA>().Modelo));
builder.Services.AddSingleton<AssistenteSuporte>();

var app = builder.Build();

app.MapPost("/perguntas", async (Pergunta pergunta, AssistenteSuporte assistente, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(pergunta.Texto) || pergunta.Texto.Length > 2000)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["texto"] = ["Informe uma pergunta com até 2000 caracteres."] });

    var resposta = await assistente.ResponderAsync(pergunta.Texto, cancellationToken);

    return resposta.Estruturada is { } estruturada
        ? Results.Ok(estruturada)
        : Results.Problem("O modelo não devolveu a resposta no formato do contrato.", statusCode: StatusCodes.Status502BadGateway);
});

app.Run();

public sealed record Pergunta(string Texto);

public partial class Program;
