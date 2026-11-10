using System.Security.Cryptography;
using System.Text;
using Api.Rag.Application.Base;
using Api.Rag.Domain.Documentos;
using Api.Rag.Domain.Segmentacao;

namespace Api.Rag.Application.Ingestao;

public sealed record ComandoIngestao(
    byte[] Conteudo,
    string NomeArquivo,
    string? Classificacao,
    string? TipoDocumento,
    string? Titulo,
    string? Origem,
    string? Versao,
    DateOnly? VigenciaInicio,
    DateOnly? VigenciaFim,
    string Usuario,
    string CorrelacaoId);

public sealed record ResultadoIngestao(Documento Documento, bool JaExistia);

public enum ResultadoOperacao
{
    Aceita,
    NaoEncontrado,
    Conflito,
}

/// <summary>Ingestão assíncrona: recebe, enfileira e processa (research R-18; FR-060, FR-061).</summary>
public sealed class IngestaoHandler(
    IRepositorioDocumentos repositorio,
    IExtratorDocumento extrator,
    IGeradorEmbeddings embeddings,
    IFilaIngestao fila,
    ParametrosSegmentacao parametros,
    TimeProvider relogio)
{
    public const string TipoPdf = "application/pdf";
    public const string TipoMarkdown = "text/markdown";
    public const string TipoTexto = "text/plain";

    /// <summary>O maior registro do Guia passa de 3.800 tokens; 32 por chamada fica abaixo do limite por requisição.</summary>
    private const int LoteEmbeddings = 32;
    private const string MotivoFalhaTecnica = "Falha técnica ao processar o documento. Reprocesse ou envie de novo.";

    private static readonly byte[] AssinaturaPdf = "%PDF-"u8.ToArray();

    /// <summary>Valida os metadados e o arquivo, grava e enfileira. Mesmo conteúdo devolve o documento existente.</summary>
    public async Task<ResultadoIngestao> ReceberAsync(ComandoIngestao comando, CancellationToken cancelamento)
    {
        if (!Enum.TryParse<ClassificacaoDocumento>(comando.Classificacao, ignoreCase: true, out var classificacao)
            || !Enum.IsDefined(classificacao)
            || comando.Classificacao!.Any(char.IsDigit))
        {
            throw new DocumentoInvalidoException("classificacao é obrigatória: oficial ou interno.");
        }

        var tipoConteudo = DetectarTipo(comando.Conteudo, comando.NomeArquivo);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(comando.Conteudo));
        var documento = Documento.Criar(
            Guid.CreateVersion7(),
            classificacao,
            comando.TipoDocumento ?? "",
            comando.Titulo ?? "",
            comando.Origem ?? "",
            comando.Versao ?? "",
            comando.VigenciaInicio,
            comando.VigenciaFim,
            sha256,
            relogio.GetUtcNow(),
            comando.Usuario);

        if (await repositorio.ObterPorShaAsync(sha256, cancelamento) is { } existente)
        {
            return new ResultadoIngestao(existente, JaExistia: true);
        }

        if (!await repositorio.AdicionarAsync(documento, new ArquivoDocumento(comando.NomeArquivo, tipoConteudo, comando.Conteudo), cancelamento))
        {
            // Outra requisição gravou o mesmo arquivo entre a consulta e a gravação.
            return new ResultadoIngestao((await repositorio.ObterPorShaAsync(sha256, cancelamento))!, JaExistia: true);
        }

        await fila.EnfileirarAsync(new ItemIngestao(documento.Id, comando.CorrelacaoId), cancelamento);
        return new ResultadoIngestao(documento, JaExistia: false);
    }

    /// <summary>Extrai, segmenta, gera os embeddings que faltam e sincroniza os trechos.</summary>
    public async Task ProcessarAsync(ItemIngestao item, CancellationToken cancelamento)
    {
        var documento = await repositorio.ObterAsync(item.DocumentoId, cancelamento);
        if (documento is null or { Status: StatusDocumento.Inativo })
        {
            return;
        }

        documento.IniciarProcessamento();
        await repositorio.AtualizarAsync(documento, cancelamento);

        try
        {
            var arquivo = await repositorio.ObterArquivoAsync(documento.Id, cancelamento)
                ?? throw new InvalidOperationException("Arquivo original não encontrado.");
            var trechos = Segmentar(documento, extrator.Extrair(arquivo));
            if (trechos.Count == 0)
            {
                documento.Falhar("Nenhum texto foi extraído do arquivo.");
                await repositorio.AtualizarAsync(documento, cancelamento);
                return;
            }

            // Com o mesmo modelo, só o texto novo recebe embedding; trocar o modelo refaz todos (P-10).
            var mesmoModelo = documento.ModeloEmbedding == embeddings.Modelo;
            var existentes = mesmoModelo ? await repositorio.ObterHashesAsync(documento.Id, cancelamento) : new HashSet<string>();
            var novos = trechos.Where(t => !existentes.Contains(t.TextoHash)).ToList();
            var vetores = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (var lote in novos.Chunk(LoteEmbeddings))
            {
                var gerados = await embeddings.GerarAsync([.. lote.Select(t => TextoParaEmbedding(documento, t))], documento.Id, item.CorrelacaoId, cancelamento);
                for (var i = 0; i < lote.Length; i++)
                {
                    vetores[lote[i].TextoHash] = gerados[i];
                }
            }

            await repositorio.SincronizarTrechosAsync(
                documento,
                [.. trechos.Select(t => new TrechoParaGravar(t, vetores.GetValueOrDefault(t.TextoHash)))],
                cancelamento);
            documento.Ativar(embeddings.Modelo, embeddings.Dimensao);
            await repositorio.AtualizarAsync(documento, cancelamento);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            documento.Falhar(MotivoFalhaTecnica);
            await repositorio.AtualizarAsync(documento, CancellationToken.None);
            throw;
        }
    }

    public async Task<ResultadoOperacao> ReprocessarAsync(Guid documentoId, string correlacaoId, CancellationToken cancelamento)
    {
        var documento = await repositorio.ObterAsync(documentoId, cancelamento);
        if (documento is null)
        {
            return ResultadoOperacao.NaoEncontrado;
        }

        if (documento.Status is not (StatusDocumento.Ativo or StatusDocumento.Falha))
        {
            return ResultadoOperacao.Conflito;
        }

        documento.IniciarProcessamento();
        await repositorio.AtualizarAsync(documento, cancelamento);
        await fila.EnfileirarAsync(new ItemIngestao(documento.Id, correlacaoId), cancelamento);
        return ResultadoOperacao.Aceita;
    }

    public async Task<ResultadoOperacao> DesativarAsync(Guid documentoId, CancellationToken cancelamento)
    {
        var documento = await repositorio.ObterAsync(documentoId, cancelamento);
        if (documento is null)
        {
            return ResultadoOperacao.NaoEncontrado;
        }

        if (documento.Status is not (StatusDocumento.Ativo or StatusDocumento.Falha or StatusDocumento.Inativo))
        {
            return ResultadoOperacao.Conflito;
        }

        if (documento.Status != StatusDocumento.Inativo)
        {
            documento.Desativar();
            await repositorio.AtualizarAsync(documento, cancelamento);
        }

        return ResultadoOperacao.Aceita;
    }

    private IReadOnlyList<Trecho> Segmentar(Documento documento, ConteudoExtraido conteudo) =>
        conteudo.Paginas is { } paginas
            ? SegmentadorGuia.Segmentar(paginas, parametros)
            : SegmentadorGenerico.Segmentar(conteudo.Texto ?? "", parametros);

    /// <summary>O caminho no documento entra no embedding: ajuda a recuperar o registro certo.</summary>
    private static string TextoParaEmbedding(Documento documento, Trecho trecho)
    {
        var caminho = new StringBuilder(documento.Titulo);
        var l = trecho.Localizacao;
        foreach (var parte in new[] { l.Capitulo is null ? null : $"Capítulo {l.Capitulo}", l.Secao, l.Registro is null ? null : $"Registro {l.Registro}" })
        {
            if (parte is not null)
            {
                caminho.Append(" > ").Append(parte);
            }
        }

        return $"{caminho}\n{trecho.Texto}";
    }

    /// <summary>Aceita PDF pela assinatura e Markdown ou texto em UTF-8 (FR-060).</summary>
    private static string DetectarTipo(byte[] conteudo, string nome)
    {
        if (conteudo.Length == 0)
        {
            throw new DocumentoInvalidoException("O arquivo está vazio.");
        }

        if (conteudo.AsSpan().StartsWith(AssinaturaPdf))
        {
            return TipoPdf;
        }

        var extensao = Path.GetExtension(nome).ToLowerInvariant();
        if (extensao is not (".md" or ".txt"))
        {
            throw new DocumentoInvalidoException("Envie PDF, Markdown (.md) ou texto (.txt).");
        }

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(conteudo);
        }
        catch (DecoderFallbackException)
        {
            throw new DocumentoInvalidoException("Markdown e texto devem estar em UTF-8.");
        }

        return extensao == ".md" ? TipoMarkdown : TipoTexto;
    }
}
