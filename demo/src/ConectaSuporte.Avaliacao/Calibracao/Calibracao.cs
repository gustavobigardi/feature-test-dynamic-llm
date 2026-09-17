using ConectaSuporte.Avaliacao.Portao;

namespace ConectaSuporte.Avaliacao.Calibracao;

/// <summary>Uma resposta julgada por uma pessoa. É contra isso que o juiz é medido.</summary>
public sealed record RotuloHumano
{
    public required string Id { get; init; }

    public required string CasoId { get; init; }

    public required RespostaSuporte Resposta { get; init; }

    public required bool AprovadoPorHumano { get; init; }

    public required string Motivo { get; init; }

    /// <summary>Quem revisou o rótulo. Rótulo sem revisor ainda é hipótese, não gabarito.</summary>
    public string? RevisadoPor { get; init; }
}

/// <param name="FalsosAceites">Humano reprovou e o juiz aprovou: o erro que deixa resposta ruim chegar ao cliente.</param>
/// <param name="FalsasRejeicoes">Humano aprovou e o juiz reprovou: custa retrabalho, não cliente.</param>
/// <param name="Kappa">Kappa de Cohen: concordância descontando a que aconteceria por acaso.</param>
public sealed record Concordancia(int AmbosAprovam, int AmbosReprovam, int FalsosAceites, int FalsasRejeicoes, double Taxa, double Kappa)
{
    public int Total => AmbosAprovam + AmbosReprovam + FalsosAceites + FalsasRejeicoes;

    public static Concordancia Calcular(IReadOnlyList<(bool Humano, bool Juiz)> pares)
    {
        if (pares.Count == 0)
            throw new ArgumentException("Sem pares para calcular concordância.", nameof(pares));

        var total = (double)pares.Count;
        var observada = pares.Count(p => p.Humano == p.Juiz) / total;

        var humanoAprova = pares.Count(p => p.Humano) / total;
        var juizAprova = pares.Count(p => p.Juiz) / total;
        var porAcaso = humanoAprova * juizAprova + (1 - humanoAprova) * (1 - juizAprova);
        var kappa = porAcaso >= 1 ? (observada >= 1 ? 1 : 0) : (observada - porAcaso) / (1 - porAcaso);

        return new Concordancia(
            AmbosAprovam: pares.Count(p => p.Humano && p.Juiz),
            AmbosReprovam: pares.Count(p => !p.Humano && !p.Juiz),
            FalsosAceites: pares.Count(p => !p.Humano && p.Juiz),
            FalsasRejeicoes: pares.Count(p => p.Humano && !p.Juiz),
            observada,
            kappa);
    }
}

public sealed record Divergencia(string RotuloId, string CasoId, bool Critico, bool Humano, bool Juiz, string MotivoHumano, string? MotivoJuiz);

public sealed record ResultadoCalibracao(
    Concordancia Concordancia,
    IReadOnlyList<Divergencia> Divergencias,
    int FalsosAceitesCriticos,
    int RotulosNaoRevisados,
    CriterioCalibracao Criterio)
{
    public bool Aprovada =>
        Concordancia.Taxa >= Criterio.ConcordanciaMinima &&
        Concordancia.Kappa >= Criterio.KappaMinimo &&
        FalsosAceitesCriticos <= Criterio.FalsosAceitesCriticosMaximos;
}
