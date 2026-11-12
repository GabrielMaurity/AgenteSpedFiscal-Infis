using System.Text.Encodings.Web;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Api.Agente.Infrastructure.Observabilidade;

/// <summary>
/// Escreve cada log como uma linha JSON no stdout, onde o Vercel e o Docker os coletam. Roda depois do
/// <see cref="MascaramentoLogProcessor"/>, então recebe textos já mascarados; a exceção, se ainda vier no
/// registro, é mascarada aqui (P-18).
/// </summary>
internal sealed class ExportadorLogConsole(TextWriter saida) : BaseExporter<LogRecord>
{
    private static readonly JsonWriterOptions Opcoes = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Lock _trava = new();

    public override ExportResult Export(in Batch<LogRecord> lote)
    {
        foreach (var registro in lote)
        {
            using var memoria = new MemoryStream();
            using (var json = new Utf8JsonWriter(memoria, Opcoes))
            {
                json.WriteStartObject();
                json.WriteString("timestamp", registro.Timestamp);
                json.WriteString("nivel", registro.LogLevel.ToString());
                json.WriteString("categoria", registro.CategoryName);
                json.WriteString("mensagem", registro.FormattedMessage ?? registro.Body);
                if (registro.TraceId != default)
                {
                    json.WriteString("traceId", registro.TraceId.ToHexString());
                }

                var excecao = registro.Exception is { } e
                    ? MascaradorDadosSensiveis.Mascarar(e.ToString())
                    : registro.Attributes?.FirstOrDefault(a => a.Key == "exception.stacktrace").Value as string;
                if (excecao is not null)
                {
                    json.WriteString("excecao", excecao);
                }

                json.WriteEndObject();
            }

            var linha = System.Text.Encoding.UTF8.GetString(memoria.ToArray());
            lock (_trava)
            {
                saida.WriteLine(linha);
                saida.Flush();
            }
        }

        return ExportResult.Success;
    }
}
