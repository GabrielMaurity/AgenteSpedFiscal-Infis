using System.Buffers;
using System.Diagnostics;

namespace Api.Rag.Endpoints.Infraestrutura;

/// <summary>Propaga X-Correlation-Id e o inclui em logs, traces e respostas de erro (P-18).</summary>
public sealed class CorrelacaoMiddleware(RequestDelegate proximo, ILogger<CorrelacaoMiddleware> logger)
{
    public const string Cabecalho = "X-Correlation-Id";
    public const string ChaveItens = "correlacaoId";

    private const int TamanhoMaximo = 128;

    private static readonly SearchValues<char> CaracteresValidos =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.");

    public async Task InvokeAsync(HttpContext contexto)
    {
        var correlacaoId = Normalizar(contexto.Request.Headers[Cabecalho].ToString());

        contexto.Items[ChaveItens] = correlacaoId;
        contexto.Response.Headers[Cabecalho] = correlacaoId;
        Activity.Current?.SetTag("correlacao.id", correlacaoId);

        using (logger.BeginScope(new Dictionary<string, object> { [ChaveItens] = correlacaoId }))
        {
            await proximo(contexto);
        }
    }

    /// <summary>Valor recebido só é aceito se for seguro para logs e cabeçalhos; senão, gera outro.</summary>
    public static string Normalizar(string? recebido) =>
        !string.IsNullOrEmpty(recebido)
        && recebido.Length <= TamanhoMaximo
        && !recebido.AsSpan().ContainsAnyExcept(CaracteresValidos)
            ? recebido
            : Guid.NewGuid().ToString("N");
}

public static class CorrelacaoExtensions
{
    /// <summary>Respostas de erro no formato RFC 9457 com correlacaoId.</summary>
    public static IServiceCollection AddErrosPadronizados(this IServiceCollection services) =>
        services.AddProblemDetails(opcoes => opcoes.CustomizeProblemDetails = contexto =>
        {
            if (contexto.HttpContext.Items.TryGetValue(CorrelacaoMiddleware.ChaveItens, out var correlacaoId))
            {
                contexto.ProblemDetails.Extensions["correlacaoId"] = correlacaoId;
            }
        });
}
