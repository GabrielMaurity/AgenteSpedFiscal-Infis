using System.Globalization;
using AgenteSpedFiscal.Contracts.Mcp;
using Api.Rag.Application.Base;
using Api.Rag.Domain.Documentos;
using LocalizacaoContrato = AgenteSpedFiscal.Contracts.Mcp.LocalizacaoTrecho;

namespace Api.Rag.Application.Busca;

public sealed class RequisicaoInvalidaException(string mensagem) : Exception(mensagem);

public sealed class TrechoNaoEncontradoException() : Exception(ContratoMcp.ErroNaoEncontrado);

public sealed record ParametrosBusca(int TopKPadrao);

/// <summary>Casos de uso das ferramentas MCP (contrato mcp-api-rag.md). Somente leitura.</summary>
public sealed class ConsultaBase(
    IBuscaTrechos busca,
    IRepositorioDocumentos repositorio,
    IGeradorEmbeddings embeddings,
    ParametrosBusca parametros)
{
    public async Task<BuscarTrechosResponse> BuscarTrechosAsync(BuscarTrechosRequest requisicao, CancellationToken cancelamento)
    {
        if (requisicao.Validar() is { } erro)
        {
            throw new RequisicaoInvalidaException(erro);
        }

        var (inicio, fim) = Periodo(requisicao.Periodo);
        var classificacoes = requisicao.Classificacoes is { Count: > 0 } lista
            ? lista.Select(ParaDominio).ToHashSet()
            : [ClassificacaoDocumento.Oficial, ClassificacaoDocumento.Interno];
        var vetor = (await embeddings.GerarAsync([requisicao.Consulta], null, requisicao.CorrelacaoId, cancelamento))[0];

        var encontrados = await busca.BuscarAsync(
            new ConsultaTrechos(
                requisicao.Consulta,
                vetor,
                embeddings.Modelo,
                requisicao.TipoDocumento,
                string.IsNullOrWhiteSpace(requisicao.Versao) ? null : requisicao.Versao,
                inicio,
                fim,
                classificacoes,
                requisicao.TopK ?? parametros.TopKPadrao),
            cancelamento);

        return new BuscarTrechosResponse(ContratoMcp.Versao, [.. encontrados.Select(Mapear)], encontrados.Count == 0);
    }

    public async Task<ObterTrechoResponse> ObterTrechoAsync(ObterTrechoRequest requisicao, CancellationToken cancelamento)
    {
        var trecho = await busca.ObterAsync(requisicao.TrechoId, cancelamento) ?? throw new TrechoNaoEncontradoException();
        return new ObterTrechoResponse(ContratoMcp.Versao, Mapear(trecho));
    }

    public async Task<ListarDocumentosResponse> ListarDocumentosAsync(ListarDocumentosRequest requisicao, CancellationToken cancelamento)
    {
        if (string.IsNullOrWhiteSpace(requisicao.CorrelacaoId))
        {
            throw new RequisicaoInvalidaException("correlacaoId é obrigatório.");
        }

        var (inicio, fim) = Periodo(requisicao.Periodo);
        var documentos = await repositorio.ListarAsync(
            new FiltroDocumentos(
                requisicao.TipoDocumento,
                requisicao.Classificacao is { } c ? ParaDominio(c) : null,
                StatusDocumento.Ativo,
                inicio,
                fim),
            cancelamento);

        return new ListarDocumentosResponse(
            ContratoMcp.Versao,
            [.. documentos.Select(d => new DocumentoAtivo(d.Id, ParaContrato(d.Classificacao), d.Titulo, d.Versao, d.VigenciaInicio, d.VigenciaFim))]);
    }

    /// <summary>"AAAA-MM" vira o primeiro e o último dia do mês.</summary>
    private static (DateOnly? Inicio, DateOnly? Fim) Periodo(string? periodo)
    {
        if (string.IsNullOrWhiteSpace(periodo))
        {
            return (null, null);
        }

        if (!DateOnly.TryParseExact($"{periodo}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var inicio))
        {
            throw new RequisicaoInvalidaException("periodo deve estar no formato AAAA-MM.");
        }

        return (inicio, inicio.AddMonths(1).AddDays(-1));
    }

    private static TrechoRecuperado Mapear(TrechoEncontrado t) => new(
        t.TrechoId,
        t.Texto,
        ParaContrato(t.Documento.Classificacao),
        new DocumentoResumo(t.Documento.Id, t.Documento.Titulo, t.Documento.Versao, t.Documento.TipoDocumento),
        new LocalizacaoContrato(t.Localizacao.Capitulo, t.Localizacao.Secao, t.Localizacao.Registro, t.Localizacao.PaginaInicio, t.Localizacao.PaginaFim),
        t.Pontuacao,
        t.Similaridade);

    private static ClassificacaoDocumento ParaDominio(Classificacao c) =>
        c == Classificacao.Oficial ? ClassificacaoDocumento.Oficial : ClassificacaoDocumento.Interno;

    private static Classificacao ParaContrato(ClassificacaoDocumento c) =>
        c == ClassificacaoDocumento.Oficial ? Classificacao.Oficial : Classificacao.Interno;
}
