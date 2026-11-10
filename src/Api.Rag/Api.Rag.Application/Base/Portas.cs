using Api.Rag.Domain.Documentos;
using Api.Rag.Domain.Segmentacao;

namespace Api.Rag.Application.Base;

/// <summary>Arquivo original, guardado para permitir o reprocessamento (FR-061).</summary>
public sealed record ArquivoDocumento(string Nome, string TipoConteudo, byte[] Conteudo);

/// <summary>Texto extraído: páginas para PDF; texto corrido para Markdown e texto simples.</summary>
public sealed record ConteudoExtraido(IReadOnlyList<PaginaExtraida>? Paginas, string? Texto);

/// <summary>Trecho a gravar. Embedding nulo mantém o vetor já gravado para o mesmo texto.</summary>
public sealed record TrechoParaGravar(Trecho Trecho, float[]? Embedding);

public sealed record FiltroDocumentos(
    string? TipoDocumento = null,
    ClassificacaoDocumento? Classificacao = null,
    StatusDocumento? Status = null,
    DateOnly? PeriodoInicio = null,
    DateOnly? PeriodoFim = null);

public interface IRepositorioDocumentos
{
    Task<Documento?> ObterAsync(Guid id, CancellationToken cancelamento);

    Task<Documento?> ObterPorShaAsync(string sha256, CancellationToken cancelamento);

    Task<IReadOnlyList<Documento>> ListarAsync(FiltroDocumentos filtro, CancellationToken cancelamento);

    /// <summary>Retorna falso quando outro documento com o mesmo SHA-256 já foi gravado (FR-061).</summary>
    Task<bool> AdicionarAsync(Documento documento, ArquivoDocumento arquivo, CancellationToken cancelamento);

    Task AtualizarAsync(Documento documento, CancellationToken cancelamento);

    Task<ArquivoDocumento?> ObterArquivoAsync(Guid documentoId, CancellationToken cancelamento);

    /// <summary>Hash do texto de cada trecho gravado do documento.</summary>
    Task<IReadOnlySet<string>> ObterHashesAsync(Guid documentoId, CancellationToken cancelamento);

    /// <summary>
    /// Deixa o documento exatamente com estes trechos: mantém o id dos que já existem (mesmo texto),
    /// insere os novos e apaga os que sumiram, sem duplicar (FR-061).
    /// </summary>
    Task SincronizarTrechosAsync(Documento documento, IReadOnlyList<TrechoParaGravar> trechos, CancellationToken cancelamento);

    Task<int> ContarTrechosAsync(Guid documentoId, CancellationToken cancelamento);

    /// <summary>Documentos recebidos ou em processamento, para retomar depois de um reinício.</summary>
    Task<IReadOnlyList<Guid>> ListarPendentesAsync(CancellationToken cancelamento);
}

public interface IExtratorDocumento
{
    ConteudoExtraido Extrair(ArquivoDocumento arquivo);
}

public interface IGeradorEmbeddings
{
    string Modelo { get; }

    int Dimensao { get; }

    /// <summary>Um vetor por texto, na mesma ordem. Registra o consumo de cada chamada (P-17).</summary>
    Task<IReadOnlyList<float[]>> GerarAsync(IReadOnlyList<string> textos, Guid? documentoId, string correlacaoId, CancellationToken cancelamento);
}

public sealed record ItemIngestao(Guid DocumentoId, string CorrelacaoId);

public interface IFilaIngestao
{
    ValueTask EnfileirarAsync(ItemIngestao item, CancellationToken cancelamento);
}

public sealed record ConsultaTrechos(
    string Texto,
    float[] Vetor,
    string ModeloEmbedding,
    string TipoDocumento,
    string? Versao,
    DateOnly? PeriodoInicio,
    DateOnly? PeriodoFim,
    IReadOnlySet<ClassificacaoDocumento> Classificacoes,
    int TopK);

public sealed record TrechoEncontrado(Guid TrechoId, string Texto, Documento Documento, LocalizacaoTrecho Localizacao, double Pontuacao);

public interface IBuscaTrechos
{
    Task<IReadOnlyList<TrechoEncontrado>> BuscarAsync(ConsultaTrechos consulta, CancellationToken cancelamento);

    /// <summary>Nulo quando o trecho não existe ou o documento não está ativo.</summary>
    Task<TrechoEncontrado?> ObterAsync(Guid trechoId, CancellationToken cancelamento);
}
