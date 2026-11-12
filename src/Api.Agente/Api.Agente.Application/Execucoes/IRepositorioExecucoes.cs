using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Execucoes;

/// <summary>Persistência de execuções. Toda operação é restrita à organização (FR-080).</summary>
public interface IRepositorioExecucoes
{
    Task SalvarAsync(Execucao execucao, CancellationToken cancelamento);

    Task<Execucao?> ObterAsync(string orgId, Guid id, CancellationToken cancelamento);

    Task<IReadOnlyList<Execucao>> ListarAsync(string orgId, int pagina, int tamanho, CancellationToken cancelamento);

    Task<bool> ExcluirAsync(string orgId, Guid id, CancellationToken cancelamento);

    /// <summary>
    /// Execuções não finalizadas de todas as organizações; uso exclusivo na recuperação. Com
    /// <paramref name="atualizadasAntesDe"/>, só as sem atualização desde então (feature 002, FR-P06).
    /// </summary>
    Task<IReadOnlyList<Execucao>> ListarEmAndamentoAsync(DateTimeOffset? atualizadasAntesDe, CancellationToken cancelamento);
}
