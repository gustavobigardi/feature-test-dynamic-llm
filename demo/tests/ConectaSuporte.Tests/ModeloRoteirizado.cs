using Microsoft.Extensions.AI;

namespace ConectaSuporte.Tests;

/// <summary>IChatClient falso: devolve o texto que o roteiro mandar. Nada sai da máquina, nada é cobrado.</summary>
public sealed class ModeloRoteirizado(Func<IReadOnlyList<ChatMessage>, string> roteiro) : IChatClient
{
    private int _chamadas;

    public int Chamadas => _chamadas;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _chamadas);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, roteiro([.. messages])))
        {
            ModelId = options?.ModelId,
            Usage = new UsageDetails { InputTokenCount = 1_000, OutputTokenCount = 100, TotalTokenCount = 1_100 },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
