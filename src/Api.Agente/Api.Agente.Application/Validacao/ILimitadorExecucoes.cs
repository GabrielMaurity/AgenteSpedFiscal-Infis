namespace Api.Agente.Application.Validacao;

/// <summary>Limita execuções simultâneas por organização (research R-26).</summary>
public interface ILimitadorExecucoes
{
    /// <summary>Reserva uma vaga. Retorna false quando a organização atingiu o limite.</summary>
    bool TentarReservar(string orgId);

    void Liberar(string orgId);
}
