using System.Globalization;
using Api.Agente.Application.TabelasReferencia;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Endpoints.Validacoes;

namespace Api.Agente.Endpoints.TabelasReferencia;

/// <summary>Importação e consulta da tabela oficial de NCM (FR-063; contrato api-agente.openapi.yaml).</summary>
public static class NcmEndpoints
{
    /// <summary>A tabela de NCM é pequena perto de um arquivo EFD: o limite evita carregar envio indevido na memória.</summary>
    private const long TamanhoMaximoBytes = 50 * 1024 * 1024;

    private const int TamanhoMaximoTexto = 200;

    public static IEndpointRouteBuilder MapTabelasReferencia(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/v1/tabelas-referencia/ncm");

        grupo.MapPost("/", ImportarAsync).RequireAuthorization(Politicas.AdminBase).DisableAntiforgery();
        grupo.MapGet("/", ListarAsync).RequireAuthorization(Politicas.AnalistaOuAdmin);

        return rotas;
    }

    private static async Task<IResult> ImportarAsync(HttpContext contexto, ImportarNcmHandler handler, CancellationToken cancelamento)
    {
        if (contexto.Request.ContentLength > TamanhoMaximoBytes)
        {
            return Problema(StatusCodes.Status413PayloadTooLarge, "Arquivo acima do limite", "A tabela de NCM excede o limite de 50 MB.");
        }

        if (!contexto.Request.HasFormContentType)
        {
            return Problema(StatusCodes.Status415UnsupportedMediaType, "Formato não suportado", "Envie a tabela em multipart/form-data.");
        }

        var formulario = await contexto.Request.ReadFormAsync(cancelamento);
        var arquivo = formulario.Files["arquivo"];
        var origem = formulario["origem"].ToString().Trim();
        var versao = formulario["versao"].ToString().Trim();
        if (arquivo is null || origem.Length == 0 || versao.Length == 0
            || origem.Length > TamanhoMaximoTexto || versao.Length > TamanhoMaximoTexto)
        {
            return Problema(StatusCodes.Status400BadRequest, "Dados inválidos", "Informe arquivo, origem, versão e início da vigência.");
        }

        if (!TentarData(formulario["vigenciaInicio"], out var inicio) || inicio is null || !TentarData(formulario["vigenciaFim"], out var fim))
        {
            return Problema(StatusCodes.Status400BadRequest, "Dados inválidos", "Informe as datas de vigência no formato AAAA-MM-DD.");
        }

        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, cancelamento);

        try
        {
            var resultado = await handler.ImportarAsync(
                new ComandoImportarNcm(memoria.ToArray(), origem, versao, inicio.Value, fim, ValidacoesEndpoints.Usuario(contexto.User)),
                cancelamento);

            return resultado.VersaoJaExistia
                ? Problema(StatusCodes.Status409Conflict, "Versão já importada", $"A versão {versao} da tabela de NCM já foi importada.")
                : Results.Created($"/api/v1/tabelas-referencia/ncm/{resultado.Tabela.Id}", resultado.Tabela);
        }
        catch (TabelaNcmInvalidaException invalida)
        {
            return Problema(StatusCodes.Status400BadRequest, "Tabela inválida", invalida.Message);
        }
    }

    private static async Task<IResult> ListarAsync(IRepositorioNcm repositorio, CancellationToken cancelamento) =>
        Results.Ok(await repositorio.ListarAsync(cancelamento));

    /// <summary>Campo vazio é válido e vira nulo; quem exige a data confere o nulo.</summary>
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

    private static IResult Problema(int status, string titulo, string detalhe) =>
        Results.Problem(detail: detalhe, statusCode: status, title: titulo);
}
