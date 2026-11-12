using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using Api.Rag.Application.Base;
using Api.Rag.Application.Ingestao;
using Api.Rag.Domain.Documentos;
using Api.Rag.Endpoints.Infraestrutura;
using Api.Rag.Endpoints.Seguranca;
using Api.Rag.Infrastructure.Configuracao;
using Microsoft.Extensions.Options;

namespace Api.Rag.Endpoints.Documentos;

public sealed record DocumentoResposta(
    Guid DocumentoId,
    string Classificacao,
    string TipoDocumento,
    string Titulo,
    string Origem,
    string Versao,
    DateOnly? VigenciaInicio,
    DateOnly? VigenciaFim,
    string Sha256,
    string Status,
    string? ModeloEmbedding,
    int? Dimensao,
    DateTimeOffset IngeridoEm,
    string IngeridoPor,
    string? MotivoFalha,
    int? QuantidadeTrechos);

/// <summary>Formulário de ingestão, só para o documento OpenAPI (contrato api-rag.openapi.yaml).</summary>
public sealed class FormularioIngestao
{
    [Required]
    [Description("Documento em PDF (.pdf), texto (.txt) ou Markdown (.md).")]
    public required IFormFile Arquivo { get; init; }

    [Required]
    [Description("oficial ou interno. Imutável depois da ingestão.")]
    public required string Classificacao { get; init; }

    [Required]
    [Description("Oficial: guia_pratico_efd_icms_ipi, nota_tecnica_leiaute, ato_cotepe, legislacao ou outro_oficial. Interno: material_interno.")]
    public required string TipoDocumento { get; init; }

    [Required]
    [Description("Título do documento, como Guia Prático EFD ICMS/IPI.")]
    public required string Titulo { get; init; }

    [Required]
    [Description("Origem do documento, como SPED ou Infis.")]
    public required string Origem { get; init; }

    [Required]
    [Description("Versão do documento, como 3.2.4.")]
    public required string Versao { get; init; }

    [Description("Início da vigência, AAAA-MM-DD.")]
    public string? VigenciaInicio { get; init; }

    [Description("Fim da vigência, AAAA-MM-DD.")]
    public string? VigenciaFim { get; init; }
}

/// <summary>Administração da base de informação (contrato api-rag.openapi.yaml). Somente admin_base.</summary>
public static class DocumentosEndpoints
{
    public static IEndpointRouteBuilder MapDocumentos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/v1/documentos").RequireAuthorization(Politicas.AdminBase);

        // O endpoint lê o formulário direto do HttpContext; os metadados abaixo só descrevem o contrato
        // no OpenAPI, para o Swagger exibir o envio do arquivo e dos campos.
        grupo.MapPost("/", IngerirAsync)
            .DisableAntiforgery()
            .Accepts<FormularioIngestao>("multipart/form-data")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        grupo.MapGet("/", ListarAsync);
        grupo.MapGet("/{documentoId:guid}", ObterAsync);
        grupo.MapPost("/{documentoId:guid}/reprocessar", ReprocessarAsync);
        grupo.MapPost("/{documentoId:guid}/desativar", DesativarAsync);

        return rotas;
    }

    private static async Task<IResult> IngerirAsync(
        HttpContext contexto,
        IngestaoHandler handler,
        IOptions<IngestaoOpcoes> opcoes,
        CancellationToken cancelamento)
    {
        var limite = opcoes.Value.TamanhoMaximoArquivoBytes;
        if (contexto.Request.ContentLength > limite)
        {
            return Problema(StatusCodes.Status413PayloadTooLarge, "Arquivo acima do limite", $"O limite é de {limite / (1024 * 1024)} MB.");
        }

        if (!contexto.Request.HasFormContentType)
        {
            return Problema(StatusCodes.Status415UnsupportedMediaType, "Formato não suportado", "Envie o documento em multipart/form-data.");
        }

        var formulario = await contexto.Request.ReadFormAsync(cancelamento);
        var arquivo = formulario.Files["arquivo"];
        if (arquivo is null)
        {
            return Problema(StatusCodes.Status400BadRequest, "Arquivo ausente", "Envie o documento no campo 'arquivo'.");
        }

        if (!TentarData(formulario["vigenciaInicio"], out var inicio) || !TentarData(formulario["vigenciaFim"], out var fim))
        {
            return Problema(StatusCodes.Status400BadRequest, "Dados inválidos", "Informe as datas de vigência no formato AAAA-MM-DD.");
        }

        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, cancelamento);

        try
        {
            var resultado = await handler.ReceberAsync(
                new ComandoIngestao(
                    memoria.ToArray(),
                    arquivo.FileName,
                    Texto(formulario["classificacao"]),
                    Texto(formulario["tipoDocumento"]),
                    Texto(formulario["titulo"]),
                    Texto(formulario["origem"]),
                    Texto(formulario["versao"]),
                    inicio,
                    fim,
                    Usuario(contexto.User),
                    (string)contexto.Items[CorrelacaoMiddleware.ChaveItens]!),
                cancelamento);

            return resultado.JaExistia
                ? Results.Problem(
                    detail: "Documento idêntico já ingerido.",
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Documento já ingerido",
                    extensions: new Dictionary<string, object?> { ["documentoId"] = resultado.Documento.Id })
                : Results.Accepted($"/api/v1/documentos/{resultado.Documento.Id}", new { documentoId = resultado.Documento.Id, status = "recebido" });
        }
        catch (DocumentoInvalidoException invalido)
        {
            return Problema(StatusCodes.Status400BadRequest, "Documento inválido", invalido.Message);
        }
    }

    private static async Task<IResult> ListarAsync(
        IRepositorioDocumentos repositorio,
        CancellationToken cancelamento,
        string? tipoDocumento = null,
        string? classificacao = null,
        string? status = null)
    {
        ClassificacaoDocumento? filtroClassificacao = null;
        StatusDocumento? filtroStatus = null;
        if (classificacao is not null)
        {
            if (!Enum.TryParse<ClassificacaoDocumento>(classificacao, true, out var c) || classificacao.Any(char.IsDigit))
            {
                return Problema(StatusCodes.Status400BadRequest, "Filtro inválido", "classificacao deve ser oficial ou interno.");
            }

            filtroClassificacao = c;
        }

        if (status is not null)
        {
            if (!Enum.TryParse<StatusDocumento>(status, true, out var s) || status.Any(char.IsDigit))
            {
                return Problema(StatusCodes.Status400BadRequest, "Filtro inválido", "status deve ser recebido, processando, ativo, falha ou inativo.");
            }

            filtroStatus = s;
        }

        var documentos = await repositorio.ListarAsync(new FiltroDocumentos(tipoDocumento, filtroClassificacao, filtroStatus), cancelamento);
        return Results.Ok(documentos.Select(d => Mapear(d, null)));
    }

    private static async Task<IResult> ObterAsync(Guid documentoId, IRepositorioDocumentos repositorio, CancellationToken cancelamento)
    {
        var documento = await repositorio.ObterAsync(documentoId, cancelamento);
        return documento is null
            ? NaoEncontrado()
            : Results.Ok(Mapear(documento, await repositorio.ContarTrechosAsync(documentoId, cancelamento)));
    }

    private static async Task<IResult> ReprocessarAsync(Guid documentoId, HttpContext contexto, IngestaoHandler handler, CancellationToken cancelamento) =>
        await handler.ReprocessarAsync(documentoId, (string)contexto.Items[CorrelacaoMiddleware.ChaveItens]!, cancelamento) switch
        {
            ResultadoOperacao.Aceita => Results.Accepted($"/api/v1/documentos/{documentoId}", new { documentoId, status = "processando" }),
            ResultadoOperacao.NaoEncontrado => NaoEncontrado(),
            _ => Problema(StatusCodes.Status409Conflict, "Reprocessamento indisponível", "Só documentos ativos ou com falha podem ser reprocessados."),
        };

    private static async Task<IResult> DesativarAsync(Guid documentoId, IngestaoHandler handler, CancellationToken cancelamento) =>
        await handler.DesativarAsync(documentoId, cancelamento) switch
        {
            ResultadoOperacao.Aceita => Results.NoContent(),
            ResultadoOperacao.NaoEncontrado => NaoEncontrado(),
            _ => Problema(StatusCodes.Status409Conflict, "Documento em processamento", "Aguarde o fim do processamento para desativar."),
        };

    private static DocumentoResposta Mapear(Documento d, int? quantidadeTrechos) => new(
        d.Id,
        d.Classificacao.ToString().ToLowerInvariant(),
        d.TipoDocumento,
        d.Titulo,
        d.Origem,
        d.Versao,
        d.VigenciaInicio,
        d.VigenciaFim,
        d.Sha256,
        d.Status.ToString().ToLowerInvariant(),
        d.ModeloEmbedding,
        d.Dimensao,
        d.IngeridoEm,
        d.IngeridoPor,
        d.MotivoFalha,
        quantidadeTrechos);

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string Usuario(ClaimsPrincipal usuario) =>
        usuario.FindFirstValue(ClaimTypes.NameIdentifier) ?? usuario.FindFirstValue("sub") ?? "desconhecido";

    /// <summary>Campo vazio é válido e vira nulo.</summary>
    private static bool TentarData(string? texto, out DateOnly? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(texto.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lida))
        {
            return false;
        }

        data = lida;
        return true;
    }

    private static IResult NaoEncontrado() =>
        Problema(StatusCodes.Status404NotFound, "Documento não encontrado", "O documento não existe.");

    private static IResult Problema(int status, string titulo, string detalhe) =>
        Results.Problem(detail: detalhe, statusCode: status, title: titulo);
}
