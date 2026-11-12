using System.ComponentModel.DataAnnotations;

namespace Api.Agente.Infrastructure.Configuracao;

public sealed class OpenAiOpcoes
{
    public const string Secao = "OpenAi";

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = "";

    /// <summary>Definido em docs/adr/0003-modelo-de-analise.md; sem valor padrão no código.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ModeloAnalise { get; set; } = "";

    [Range(0.0, 2.0)]
    public double Temperatura { get; set; }

    [Range(1, 200_000)]
    public int MaximoTokensSaida { get; set; } = 4_000;
}

public sealed class MongoDbOpcoes
{
    public const string Secao = "MongoDb";

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = "";

    [Required(AllowEmptyStrings = false)]
    public string Database { get; set; } = "";
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

public sealed class McpRagOpcoes
{
    public const string Secao = "McpRag";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string Url { get; set; } = "";

    /// <summary>
    /// Token Bearer de serviço com audiência api-rag. Em desenvolvimento vem do dotnet user-jwts; o fluxo
    /// client credentials de produção depende do ADR-0004. Sem token, a chamada vai sem Authorization, o que só
    /// funciona com a Api.Rag em modo sem autenticação (Autenticacao:Desabilitada, só em Development).
    /// </summary>
    public string? TokenServico { get; set; }
}

public sealed class BaseInformacaoOpcoes
{
    public const string Secao = "BaseInformacao";

    [Required(AllowEmptyStrings = false)]
    public string TipoDocumento { get; set; } = "guia_pratico_efd_icms_ipi";

    /// <summary>Valor inicial do research R-13; fixado após os evals (T106).</summary>
    [Range(1, 20)]
    public int TopK { get; set; } = 8;

    [Range(1, 50)]
    public int ConsultasPorExecucao { get; set; } = 5;

    [Range(1, 300)]
    public int TimeoutFerramentaSegundos { get; set; } = 15;

    /// <summary>
    /// Outros tipos de documento oficial usados nas respostas além do Guia Prático (research R-39), como
    /// manuais estaduais. Entram pela vigência no período; a versão vale só para o tipo principal.
    /// </summary>
    public string[] TiposOficiaisAdicionais { get; set; } = ["nota_tecnica_leiaute", "ato_cotepe", "legislacao", "outro_oficial"];
}

public sealed class ArquivosOpcoes
{
    public const string Secao = "Arquivos";

    [Required(AllowEmptyStrings = false)]
    public string DiretorioTemporario { get; set; } = Path.Combine(Path.GetTempPath(), "agente-uploads");
}

public sealed class ResilienciaOpcoes
{
    public const string Secao = "Resiliencia";

    [Range(1, 600)]
    public int TimeoutLlmSegundos { get; set; } = 60;

    [Range(1, 120)]
    public int TimeoutMcpSegundos { get; set; } = 10;

    [Range(1, 10)]
    public int Retentativas { get; set; } = 2;

    [Range(1, 60_000)]
    public int AtrasoBaseMilissegundos { get; set; } = 2_000;
}

public sealed class LimitesOpcoes
{
    public const string Secao = "Limites";

    [Range(1L, long.MaxValue)]
    public long TamanhoMaximoArquivoBytes { get; set; } = 500L * 1024 * 1024;

    [Range(1, 1_000_000)]
    public int OcorrenciasDetalhadasPorRegra { get; set; } = 1_000;

    [Range(0, 10)]
    public int RetentativasRespostaLlm { get; set; } = 2;

    [Range(1, 100)]
    public int ChamadasFerramentaPorExecucao { get; set; } = 8;

    [Range(1, 1_000)]
    public int MensagensEnviadasAoModelo { get; set; } = 20;

    [Range(1, 100_000)]
    public int RequisicoesPorJanela { get; set; } = 60;

    [Range(1, 86_400)]
    public int JanelaSegundos { get; set; } = 60;

    [Range(1, 1_000)]
    public int ExecucoesSimultaneasPorOrganizacao { get; set; } = 2;

    [Range(1, 64)]
    public int ProcessamentosParalelos { get; set; } = 2;

    [Range(1, 10_000)]
    public int AchadosPorPagina { get; set; } = 500;
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
