using System.Text.Json;
using Api.Rag.Application.Base;
using Api.Rag.Domain.Documentos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Classificacoes = Api.Rag.Infrastructure.Persistencia.ClassificacaoDocumento;
using Estados = Api.Rag.Infrastructure.Persistencia.StatusDocumento;
using StatusDominio = Api.Rag.Domain.Documentos.StatusDocumento;

namespace Api.Rag.Infrastructure.Persistencia;

/// <summary>Conversões entre o domínio e as tabelas documento e trecho.</summary>
internal static class MapeamentoDocumento
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Documento ParaDominio(DocumentoEntidade e) => Documento.Restaurar(
        e.Id,
        e.Classificacao == Classificacoes.Oficial ? Domain.Documentos.ClassificacaoDocumento.Oficial : Domain.Documentos.ClassificacaoDocumento.Interno,
        e.TipoDocumento,
        e.Titulo,
        e.Origem,
        e.Versao,
        e.VigenciaInicio,
        e.VigenciaFim,
        e.Sha256,
        e.IngeridoEm,
        e.IngeridoPor,
        Enum.Parse<StatusDominio>(e.Status, ignoreCase: true),
        e.ModeloEmbedding,
        e.Dimensao,
        e.MotivoFalha);

    public static string Classificacao(Domain.Documentos.ClassificacaoDocumento c) =>
        c == Domain.Documentos.ClassificacaoDocumento.Oficial ? Classificacoes.Oficial : Classificacoes.Interno;

    public static string Status(StatusDominio s) => s.ToString().ToLowerInvariant();

    public static string Localizacao(LocalizacaoTrecho l) => JsonSerializer.Serialize(l, Json);

    public static LocalizacaoTrecho Localizacao(string json) =>
        JsonSerializer.Deserialize<LocalizacaoTrecho>(json, Json) ?? new LocalizacaoTrecho(null, null, null, null, null);

    public static void Copiar(Documento d, DocumentoEntidade e)
    {
        e.Status = Status(d.Status);
        e.ModeloEmbedding = d.ModeloEmbedding;
        e.Dimensao = d.Dimensao;
        e.MotivoFalha = d.MotivoFalha;
    }
}

public sealed class RepositorioDocumentos(RagDbContext banco) : IRepositorioDocumentos
{
    private const string ViolacaoDeUnicidade = "23505";

    public async Task<Documento?> ObterAsync(Guid id, CancellationToken cancelamento) =>
        await banco.Documentos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancelamento) is { } e
            ? MapeamentoDocumento.ParaDominio(e)
            : null;

    public async Task<Documento?> ObterPorShaAsync(string sha256, CancellationToken cancelamento) =>
        await banco.Documentos.AsNoTracking().FirstOrDefaultAsync(d => d.Sha256 == sha256, cancelamento) is { } e
            ? MapeamentoDocumento.ParaDominio(e)
            : null;

    public async Task<IReadOnlyList<Documento>> ListarAsync(FiltroDocumentos filtro, CancellationToken cancelamento)
    {
        var consulta = banco.Documentos.AsNoTracking();
        if (filtro.TipoDocumento is { } tipo)
        {
            consulta = consulta.Where(d => d.TipoDocumento == tipo);
        }

        if (filtro.Classificacao is { } classificacao)
        {
            var valor = MapeamentoDocumento.Classificacao(classificacao);
            consulta = consulta.Where(d => d.Classificacao == valor);
        }

        if (filtro.Status is { } status)
        {
            var valor = MapeamentoDocumento.Status(status);
            consulta = consulta.Where(d => d.Status == valor);
        }

        if (filtro.PeriodoInicio is { } inicio && filtro.PeriodoFim is { } fim)
        {
            consulta = consulta.Where(d => (d.VigenciaInicio == null || d.VigenciaInicio <= fim) && (d.VigenciaFim == null || d.VigenciaFim >= inicio));
        }

        var entidades = await consulta.OrderByDescending(d => d.IngeridoEm).ToListAsync(cancelamento);
        return entidades.ConvertAll(MapeamentoDocumento.ParaDominio);
    }

    public async Task<bool> AdicionarAsync(Documento documento, ArquivoDocumento arquivo, CancellationToken cancelamento)
    {
        var entidade = new DocumentoEntidade
        {
            Id = documento.Id,
            Classificacao = MapeamentoDocumento.Classificacao(documento.Classificacao),
            TipoDocumento = documento.TipoDocumento,
            Titulo = documento.Titulo,
            Origem = documento.Origem,
            Versao = documento.Versao,
            VigenciaInicio = documento.VigenciaInicio,
            VigenciaFim = documento.VigenciaFim,
            Sha256 = documento.Sha256,
            Status = Estados.Recebido,
            IngeridoEm = documento.IngeridoEm.ToUniversalTime(),
            IngeridoPor = documento.IngeridoPor,
        };
        MapeamentoDocumento.Copiar(documento, entidade);
        banco.Documentos.Add(entidade);
        banco.Arquivos.Add(new DocumentoArquivoEntidade
        {
            DocumentoId = documento.Id,
            Nome = arquivo.Nome,
            TipoConteudo = arquivo.TipoConteudo,
            Conteudo = arquivo.Conteudo,
        });

        try
        {
            await banco.SaveChangesAsync(cancelamento);
            return true;
        }
        catch (DbUpdateException excecao) when (excecao.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade })
        {
            banco.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task AtualizarAsync(Documento documento, CancellationToken cancelamento)
    {
        var entidade = await banco.Documentos.FirstAsync(d => d.Id == documento.Id, cancelamento);
        MapeamentoDocumento.Copiar(documento, entidade);
        await banco.SaveChangesAsync(cancelamento);
    }

    public async Task<ArquivoDocumento?> ObterArquivoAsync(Guid documentoId, CancellationToken cancelamento) =>
        await banco.Arquivos.AsNoTracking().FirstOrDefaultAsync(a => a.DocumentoId == documentoId, cancelamento) is { } a
            ? new ArquivoDocumento(a.Nome, a.TipoConteudo, a.Conteudo)
            : null;

    public async Task<IReadOnlySet<string>> ObterHashesAsync(Guid documentoId, CancellationToken cancelamento) =>
        (await banco.Trechos.AsNoTracking().Where(t => t.DocumentoId == documentoId).Select(t => t.TextoHash).ToListAsync(cancelamento))
            .ToHashSet(StringComparer.Ordinal);

    public async Task SincronizarTrechosAsync(Documento documento, IReadOnlyList<TrechoParaGravar> trechos, CancellationToken cancelamento)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(cancelamento);
        var existentes = await banco.Trechos.Where(t => t.DocumentoId == documento.Id).ToDictionaryAsync(t => t.TextoHash, StringComparer.Ordinal, cancelamento);
        var classificacao = MapeamentoDocumento.Classificacao(documento.Classificacao);
        var mantidos = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (trecho, embedding) in trechos)
        {
            mantidos.Add(trecho.TextoHash);
            if (existentes.TryGetValue(trecho.TextoHash, out var atual))
            {
                // O id é preservado: citações antigas continuam apontando para o mesmo trecho.
                atual.Ordem = trecho.Ordem;
                atual.Localizacao = MapeamentoDocumento.Localizacao(trecho.Localizacao);
                if (embedding is not null)
                {
                    atual.Embedding = new Vector(embedding);
                }

                continue;
            }

            banco.Trechos.Add(new TrechoEntidade
            {
                Id = Guid.CreateVersion7(),
                DocumentoId = documento.Id,
                Ordem = trecho.Ordem,
                Classificacao = classificacao,
                Localizacao = MapeamentoDocumento.Localizacao(trecho.Localizacao),
                Texto = trecho.Texto,
                TextoHash = trecho.TextoHash,
                Embedding = new Vector(embedding ?? throw new InvalidOperationException($"Trecho novo sem embedding: {trecho.TextoHash}.")),
            });
        }

        banco.Trechos.RemoveRange(existentes.Values.Where(t => !mantidos.Contains(t.TextoHash)));
        await banco.SaveChangesAsync(cancelamento);
        await transacao.CommitAsync(cancelamento);
        banco.ChangeTracker.Clear();
    }

    public Task<int> ContarTrechosAsync(Guid documentoId, CancellationToken cancelamento) =>
        banco.Trechos.CountAsync(t => t.DocumentoId == documentoId, cancelamento);

    public async Task<IReadOnlyList<Guid>> ListarPendentesAsync(CancellationToken cancelamento) =>
        await banco.Documentos.AsNoTracking()
            .Where(d => d.Status == Estados.Recebido || d.Status == Estados.Processando)
            .OrderBy(d => d.IngeridoEm)
            .Select(d => d.Id)
            .ToListAsync(cancelamento);
}
