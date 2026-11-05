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
