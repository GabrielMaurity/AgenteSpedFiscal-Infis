using System.Text.Json;
using Api.Rag.Infrastructure.Observabilidade;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace Api.Rag.Tests.Infraestrutura;

/// <summary>
/// Sem OTLP (Vercel), os logs saem no stdout em JSON, uma linha por registro, já mascarados (P-18,
/// feature 002). É o que aparece nos Runtime Logs.
/// </summary>
public class LogConsoleTests
{
    private static (ILogger Logger, StringWriter Saida, ILoggerFactory Fabrica) Criar()
    {
        var saida = new StringWriter();
        var fabrica = LoggerFactory.Create(b => b.AddOpenTelemetry(o => ObservabilidadeExtensions.ConfigurarLogs(o, saida)));
        return (fabrica.CreateLogger("Api.Rag.Teste"), saida, fabrica);
    }

    private static JsonElement[] Linhas(StringWriter saida) =>
        [.. saida.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    [Fact]
    public void Cada_log_vira_uma_linha_json_com_nivel_categoria_e_mensagem()
    {
        var (logger, saida, fabrica) = Criar();
        using (fabrica)
        {
            logger.LogWarning("Execução {ExecucaoId} concluída com limitações.", "019a5c2e");
        }

        var linha = Assert.Single(Linhas(saida));
        Assert.Equal("Warning", linha.GetProperty("nivel").GetString());
        Assert.Equal("Api.Rag.Teste", linha.GetProperty("categoria").GetString());
        Assert.Equal("Execução 019a5c2e concluída com limitações.", linha.GetProperty("mensagem").GetString());
        Assert.True(linha.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public void Dados_sensiveis_saem_mascarados_na_mensagem_e_na_excecao()
    {
        var (logger, saida, fabrica) = Criar();
        using (fabrica)
        {
            logger.LogError(
                new InvalidOperationException("falha ao processar o documento do CNPJ 12345678000195"),
                "CNPJ {Cnpj} recusado", "12.345.678/0001-95");
        }

        var texto = saida.ToString();
        Assert.DoesNotContain("12.345.678/0001-95", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678000195", texto, StringComparison.Ordinal);
        var linha = Assert.Single(Linhas(saida));
        Assert.Equal("CNPJ *** recusado", linha.GetProperty("mensagem").GetString());
        Assert.Contains("InvalidOperationException", linha.GetProperty("excecao").GetString(), StringComparison.Ordinal);
        Assert.Contains("CNPJ ***", linha.GetProperty("excecao").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Abaixo_de_information_nao_e_escrito()
    {
        var (logger, saida, fabrica) = Criar();
        using (fabrica)
        {
            logger.LogDebug("detalhe interno");
            logger.LogInformation("evento");
        }

        Assert.Single(Linhas(saida));
    }
}
