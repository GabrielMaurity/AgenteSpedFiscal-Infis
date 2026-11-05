using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Rag.Infrastructure.Configuracao;

public sealed class OpenAiOpcoes
{
    public const string Secao = "OpenAi";

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = "";

    /// <summary>Candidato de docs/adr/0002-embeddings-e-busca-hibrida.md.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ModeloEmbedding { get; set; } = "text-embedding-3-small";

    /// <summary>Precisa coincidir com a coluna vector(1536); trocar exige migração e reprocessamento.</summary>
    [Range(1536, 1536)]
    public int Dimensao { get; set; } = 1536;
}

public sealed class PostgresOpcoes
{
    public const string Secao = "Postgres";

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = "";
}

public sealed class PersistenciaOpcoes
{
    public const string Secao = "Persistencia";

    /// <summary>Somente para ambientes locais; em outros ambientes as migrações são tarefa controlada.</summary>
    public bool AplicarMigracoesNaInicializacao { get; set; }
}

/// <summary>Valores iniciais para avaliação, ainda não aprovados (P-10, research R-13).</summary>
public sealed class BuscaOpcoes
{
    public const string Secao = "Busca";

    [Range(1, 20)]
    public int TopK { get; set; } = 8;
}

/// <summary>Valores iniciais para avaliação, ainda não aprovados (P-10, research R-13).</summary>
public sealed class SegmentacaoOpcoes
{
    public const string Secao = "Segmentacao";

    [Range(50, 8_000)]
    public int LimiteTokens { get; set; } = 800;
}

public static class ConfiguracaoExtensions
{
    /// <summary>Registra as opções e impede o início do serviço com configuração inválida (P-19).</summary>
    public static IServiceCollection AddOpcoesRag(this IServiceCollection services)
    {
        Registrar<OpenAiOpcoes>(services, OpenAiOpcoes.Secao);
        Registrar<PostgresOpcoes>(services, PostgresOpcoes.Secao);
        Registrar<PersistenciaOpcoes>(services, PersistenciaOpcoes.Secao);
        Registrar<BuscaOpcoes>(services, BuscaOpcoes.Secao);
        Registrar<SegmentacaoOpcoes>(services, SegmentacaoOpcoes.Secao);
        return services;
    }

    private static void Registrar<T>(IServiceCollection services, string secao)
        where T : class =>
        services.AddOptions<T>().BindConfiguration(secao).ValidateDataAnnotations().ValidateOnStart();
}
