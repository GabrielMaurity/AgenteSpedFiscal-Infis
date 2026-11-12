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

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string Endpoint { get; set; } = "https://api.openai.com/v1/";

    [Range(1, 300)]
    public int TimeoutSegundos { get; set; } = 60;

    /// <summary>Retentativas em erro transitório (429 e 5xx); cada tentativa gera um registro de consumo (P-17).</summary>
    [Range(0, 5)]
    public int Retentativas { get; set; } = 2;
}

public sealed class IngestaoOpcoes
{
    public const string Secao = "Ingestao";

    /// <summary>O Guia Prático 3.2.4 tem cerca de 6,5 MB.</summary>
    [Range(1, 1024L * 1024 * 1024)]
    public long TamanhoMaximoArquivoBytes { get; set; } = 100L * 1024 * 1024;
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

/// <summary>
/// Recuperação de itens interrompidos (feature 002, FR-P06). Com instância única, a inicialização trata
/// todos os itens em andamento, pois nada mais os processa. Com várias instâncias (Vercel), só os parados
/// há mais que o limiar, que deve passar da duração máxima de uma requisição na plataforma.
/// </summary>
public sealed class RecuperacaoOpcoes
{
    public const string Secao = "Recuperacao";

    public bool InstanciaUnica { get; set; } = true;

    [Range(1, 1_440)]
    public int LimiarMinutos { get; set; } = 10;

    [Range(1, 1_440)]
    public int IntervaloVarreduraMinutos { get; set; } = 1;

    /// <summary>Nulo com instância única: todos os itens em andamento são tratados.</summary>
    public TimeSpan? Limiar => InstanciaUnica ? null : TimeSpan.FromMinutes(LimiarMinutos);
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
        Registrar<IngestaoOpcoes>(services, IngestaoOpcoes.Secao);
        Registrar<RecuperacaoOpcoes>(services, RecuperacaoOpcoes.Secao);
        return services;
    }

    private static void Registrar<T>(IServiceCollection services, string secao)
        where T : class =>
        services.AddOptions<T>().BindConfiguration(secao).ValidateDataAnnotations().ValidateOnStart();
}
