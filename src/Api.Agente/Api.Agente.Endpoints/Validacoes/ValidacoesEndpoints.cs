using System.Security.Claims;
using Api.Agente.Application.Execucoes;
using Api.Agente.Application.Exportacao;
using Api.Agente.Application.Validacao;
using Api.Agente.Domain.Execucoes;
using Api.Agente.Endpoints.Infraestrutura;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Infrastructure.Configuracao;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.Agente.Endpoints.Validacoes;

/// <summary>Contrato api-agente.openapi.yaml.</summary>
public static class ValidacoesEndpoints
{
    private const string CampoArquivo = "arquivo";

    /// <summary>Folga para os cabeçalhos e campos do multipart além do próprio arquivo.</summary>
    private const long FolgaMultipartBytes = 1024 * 1024;

    private const int TamanhoMaximoPagina = 100;

    public static IEndpointRouteBuilder MapValidacoes(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/v1/validacoes");

        grupo.MapPost("/", EnviarAsync)
            .RequireAuthorization(Politicas.AnalistaOuAgente)
            .RequireRateLimiting(LimitacaoRequisicoesExtensions.PoliticaPorOrganizacao)
            .AddOpenApiOperationTransformer(DescreverFormulario)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        grupo.MapGet("/", ListarAsync).RequireAuthorization(Politicas.Analista);
        grupo.MapGet("/{execucaoId:guid}", ObterAsync).RequireAuthorization(Politicas.AnalistaOuAgente);
        grupo.MapDelete("/{execucaoId:guid}", ExcluirAsync).RequireAuthorization(Politicas.Analista);
        grupo.MapGet("/{execucaoId:guid}/exportacao.csv", ExportarAsync).RequireAuthorization(Politicas.Analista);

        return rotas;
    }

    /// <summary>
    /// Descreve o multipart só no documento OpenAPI, para o Swagger mostrar o campo do arquivo (research
    /// R-36). O endpoint lê o arquivo em fluxo, sem model binding. Não usar Accepts: ele também restringe
    /// o roteamento por Content-Type e devolveria 415 antes do limite de requisições.
    /// </summary>
    private static Task DescreverFormulario(OpenApiOperation operacao, OpenApiOperationTransformerContext contexto, CancellationToken cancelamento)
    {
        operacao.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string> { CampoArquivo },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            [CampoArquivo] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Format = "binary",
                                Description = "Arquivo da EFD ICMS/IPI em texto (.txt), como gerado para o PVA.",
                            },
                        },
                    },
                },
            },
        };
        return Task.CompletedTask;
    }

    public static string Organizacao(ClaimsPrincipal usuario) => usuario.FindFirstValue(AutenticacaoExtensions.ClaimOrganizacao)!;

    public static string Usuario(ClaimsPrincipal usuario) => usuario.FindFirstValue(ClaimTypes.NameIdentifier) ?? usuario.FindFirstValue("sub") ?? "desconhecido";

    public static string Correlacao(HttpContext contexto) => (string)contexto.Items[CorrelacaoMiddleware.ChaveItens]!;

    private static async Task<IResult> EnviarAsync(
        HttpContext contexto,
        ValidarArquivoHandler handler,
        IOptions<LimitesOpcoes> limites,
        CancellationToken cancelamento)
    {
        // Sem IFormFile, que bufferiza o arquivo: o upload de até 500 MB é lido em fluxo (research R-03).
        var tamanhoMaximo = limites.Value.TamanhoMaximoArquivoBytes + FolgaMultipartBytes;
        if (contexto.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limiteCorpo)
        {
            limiteCorpo.MaxRequestBodySize = tamanhoMaximo;
        }

        if (contexto.Request.ContentLength > tamanhoMaximo)
        {
            return Problema(StatusCodes.Status413PayloadTooLarge, "Arquivo acima do limite", $"O arquivo excede o limite de {limites.Value.TamanhoMaximoArquivoBytes / (1024 * 1024)} MB.");
        }

        if (!MediaTypeHeaderValue.TryParse(contexto.Request.ContentType, out var tipo)
            || !tipo.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(tipo.Boundary.Value))
        {
            return Problema(StatusCodes.Status415UnsupportedMediaType, "Formato não suportado", "Envie o arquivo em multipart/form-data, no campo 'arquivo'.");
        }

        var leitor = new MultipartReader(HeaderUtilities.RemoveQuotes(tipo.Boundary).Value!, contexto.Request.Body);
        Execucao? execucao = null;
        try
        {
            while (await leitor.ReadNextSectionAsync(cancelamento) is { } secao)
            {
                var arquivo = secao.AsFileSection();
                if (arquivo is not null && arquivo.Name == CampoArquivo && execucao is null)
                {
                    execucao = await handler.ReceberAsync(
                        new ComandoValidacao(
                            arquivo.FileStream!,
                            arquivo.FileName,
                            Organizacao(contexto.User),
                            Usuario(contexto.User),
                            Correlacao(contexto),
                            CanalExecucao.Rest),
                        cancelamento);
                }
            }
        }
        catch (ArquivoRecusadoException recusa)
        {
            return recusa.Motivo == MotivoRecusaArquivo.TamanhoExcedido
                ? Problema(StatusCodes.Status413PayloadTooLarge, "Arquivo acima do limite", recusa.Message)
                : Problema(StatusCodes.Status415UnsupportedMediaType, "Formato não suportado", recusa.Message);
        }
        catch (LimiteExecucoesException limite)
        {
            return Problema(StatusCodes.Status429TooManyRequests, "Limite de execuções", limite.Message);
        }

        return execucao is null
            ? Problema(StatusCodes.Status400BadRequest, "Arquivo ausente", "Envie o arquivo no campo 'arquivo'.")
            : Results.Accepted($"/api/v1/validacoes/{execucao.Id}", new { execucaoId = execucao.Id, status = EnvelopeMapper.ParaStatus(execucao.Estado) });
    }

    private static async Task<IResult> ListarAsync(
        ClaimsPrincipal usuario,
        IRepositorioExecucoes repositorio,
        CancellationToken cancelamento,
        int pagina = 1,
        int tamanho = 20)
    {
        if (pagina < 1 || tamanho is < 1 or > TamanhoMaximoPagina)
        {
            return Problema(StatusCodes.Status400BadRequest, "Paginação inválida", $"Use pagina a partir de 1 e tamanho de 1 a {TamanhoMaximoPagina}.");
        }

        var execucoes = await repositorio.ListarAsync(Organizacao(usuario), pagina, tamanho, cancelamento);
        return Results.Ok(execucoes.Select(e => new
        {
            execucaoId = e.Id,
            status = EnvelopeMapper.ParaStatus(e.Estado),
            criadoEm = e.CriadoEm,
            arquivoNome = e.Arquivo?.Nome,
            revisaoHumanaNecessaria = e.RevisaoHumanaNecessaria,
        }));
    }

    private static async Task<IResult> ObterAsync(
        Guid execucaoId,
        ClaimsPrincipal usuario,
        IRepositorioExecucoes repositorio,
        IOptions<LimitesOpcoes> limites,
        CancellationToken cancelamento,
        int paginaAchados = 1)
    {
        if (paginaAchados < 1)
        {
            return Problema(StatusCodes.Status400BadRequest, "Paginação inválida", "Use paginaAchados a partir de 1.");
        }

        var execucao = await repositorio.ObterAsync(Organizacao(usuario), execucaoId, cancelamento);
        return execucao is null
            ? NaoEncontrada()
            : Results.Ok(EnvelopeMapper.Mapear(execucao, paginaAchados, limites.Value.AchadosPorPagina));
    }

    private static async Task<IResult> ExcluirAsync(
        Guid execucaoId,
        ClaimsPrincipal usuario,
        IRepositorioExecucoes repositorio,
        CancellationToken cancelamento)
    {
        var organizacao = Organizacao(usuario);
        var execucao = await repositorio.ObterAsync(organizacao, execucaoId, cancelamento);
        if (execucao is null)
        {
            return NaoEncontrada();
        }

        // Em andamento, o processamento salvaria a execução de novo depois da exclusão.
        if (!execucao.EstaFinalizada)
        {
            return Problema(StatusCodes.Status409Conflict, "Execução em processamento", "Aguarde o fim do processamento para excluir a execução.");
        }

        await repositorio.ExcluirAsync(organizacao, execucaoId, cancelamento);
        return Results.NoContent();
    }

    private static async Task<IResult> ExportarAsync(
        Guid execucaoId,
        HttpContext contexto,
        IRepositorioExecucoes repositorio,
        CancellationToken cancelamento)
    {
        var execucao = await repositorio.ObterAsync(Organizacao(contexto.User), execucaoId, cancelamento);
        if (execucao is null)
        {
            return NaoEncontrada();
        }

        if (!execucao.EstaFinalizada)
        {
            return Problema(StatusCodes.Status409Conflict, "Execução em processamento", "O CSV fica disponível ao fim do processamento.");
        }

        return Results.Stream(
            destino => ExportadorCsv.EscreverAsync(execucao, destino, cancelamento),
            ExportadorCsv.TipoConteudo,
            $"validacao-{execucao.Id}.csv");
    }

    private static IResult NaoEncontrada() =>
        Problema(StatusCodes.Status404NotFound, "Execução não encontrada", "A execução não existe, expirou ou pertence a outra organização.");

    private static IResult Problema(int status, string titulo, string detalhe) =>
        Results.Problem(detail: detalhe, statusCode: status, title: titulo);
}
