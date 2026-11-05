using Api.Agente.Application.TabelasReferencia;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Infrastructure.Persistencia.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Api.Agente.Infrastructure.TabelasReferencia;

/// <summary>Tabelas de referência no PostgreSQL da Api.Agente (research R-20).</summary>
public sealed class RepositorioNcm(IServiceScopeFactory escopos, ILogger<RepositorioNcm> logger) : IRepositorioNcm
{
    private const string TipoNcm = "ncm";
    private const string ViolacaoDeUnicidade = "23505";

    public async Task<bool> ImportarAsync(TabelaNcmImportada tabela, IReadOnlyList<LinhaNcm> linhas, CancellationToken cancelamento)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<AgenteDbContext>();

        banco.TabelasReferencia.Add(new TabelaReferencia
        {
            Id = tabela.Id,
            Tipo = TipoNcm,
            Origem = tabela.Origem,
            Versao = tabela.Versao,
            VigenciaInicio = tabela.VigenciaInicio,
            VigenciaFim = tabela.VigenciaFim,
            Sha256 = tabela.Sha256,
            ImportadoEm = tabela.ImportadoEm,
            ImportadoPor = tabela.ImportadoPor,
        });
        banco.Ncms.AddRange(linhas.Select(l => new Ncm
        {
            Id = Guid.CreateVersion7(),
            TabelaReferenciaId = tabela.Id,
            Codigo = l.Codigo,
            Descricao = l.Descricao,
            VigenciaInicio = l.VigenciaInicio,
            VigenciaFim = l.VigenciaFim,
        }));

        try
        {
            await banco.SaveChangesAsync(cancelamento);
            return true;
        }
        catch (DbUpdateException excecao) when (excecao.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade })
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<TabelaNcmImportada>> ListarAsync(CancellationToken cancelamento)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<AgenteDbContext>();

        return await banco.TabelasReferencia
            .AsNoTracking()
            .Where(t => t.Tipo == TipoNcm)
            .OrderByDescending(t => t.VigenciaInicio)
            .Select(t => new TabelaNcmImportada(
                t.Id,
                t.Origem,
                t.Versao,
                t.VigenciaInicio,
                t.VigenciaFim,
                t.Sha256,
                t.ImportadoEm,
                t.ImportadoPor,
                banco.Ncms.Count(n => n.TabelaReferenciaId == t.Id)))
            .ToListAsync(cancelamento);
    }

    public async Task<ITabelaNcm?> CarregarVigenteAsync(DateOnly periodoInicio, DateOnly periodoFim, CancellationToken cancelamento)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var banco = escopo.ServiceProvider.GetRequiredService<AgenteDbContext>();

            var versao = await banco.TabelasReferencia
                .AsNoTracking()
                .Where(t => t.Tipo == TipoNcm
                    && t.VigenciaInicio <= periodoFim
                    && (t.VigenciaFim == null || t.VigenciaFim >= periodoInicio))
                .OrderByDescending(t => t.VigenciaInicio)
                .ThenByDescending(t => t.ImportadoEm)
                .FirstOrDefaultAsync(cancelamento);
            if (versao is null)
            {
                return null;
            }

            var codigos = await banco.Ncms
                .AsNoTracking()
                .Where(n => n.TabelaReferenciaId == versao.Id)
                .Select(n => new CodigoNcm(n.Codigo, n.VigenciaInicio, n.VigenciaFim))
                .ToListAsync(cancelamento);

            return new TabelaNcm(versao.Versao, codigos, periodoInicio, periodoFim);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            // Dependência indisponível não derruba a validação: a regra fica como não realizada.
            logger.LogWarning(excecao, "Tabela de NCM indisponível.");
            return null;
        }
    }
}
