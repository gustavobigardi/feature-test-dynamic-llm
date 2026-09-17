namespace ConectaSuporte.Avaliacao;

/// <summary>Localiza a raiz do repositório para ler ia/ e evals/ sem copiar arquivos para bin/.</summary>
public static class Repositorio
{
    public static string Raiz { get; } = Encontrar();

    public static string Caminho(params string[] partes) => Path.Combine([Raiz, .. partes]);

    private static string Encontrar()
    {
        for (var diretorio = new DirectoryInfo(AppContext.BaseDirectory); diretorio is not null; diretorio = diretorio.Parent)
        {
            if (File.Exists(Path.Combine(diretorio.FullName, "ConectaSuporte.slnx")))
                return diretorio.FullName;
        }

        throw new InvalidOperationException($"ConectaSuporte.slnx não encontrado acima de {AppContext.BaseDirectory}.");
    }
}
