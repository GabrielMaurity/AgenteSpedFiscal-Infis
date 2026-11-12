using Api.Agente.Domain.Analise;

namespace Api.Agente.Domain.Conversas;

public enum PapelMensagem
{
    Usuario,
    Agente,
}

/// <summary>Mensagem da conversa. Fundamentação, referências e proximidade são calculadas pelo sistema (P-12, R-27, R-38).</summary>
public sealed record MensagemConversa(
    Guid Id,
    PapelMensagem Papel,
    string Texto,
    List<ReferenciaTrecho> Referencias,
    bool Fundamentada,
    bool FundamentadaEmFonteOficial,
    List<string> Limitacoes,
    DateTimeOffset CriadoEm,
    string? AchadoId = null,
    string? Periodo = null,
    double? Proximidade = null);

/// <summary>Conversa isolada por organização e usuário (FR-054). O histórico nunca é fonte oficial.</summary>
public sealed record Conversa(
    Guid Id,
    string OrgId,
    string UsuarioId,
    Guid? ExecucaoId,
    List<MensagemConversa> Mensagens,
    DateTimeOffset CriadoEm,
    DateTimeOffset ExpiraEm)
{
    public const int DiasRetencao = 90;

    public static Conversa Criar(Guid id, string orgId, string usuarioId, Guid? execucaoId, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(usuarioId);
        return new Conversa(id, orgId, usuarioId, execucaoId, [], agora, agora.AddDays(DiasRetencao));
    }

    public void Adicionar(MensagemConversa mensagem) => Mensagens.Add(mensagem);
}
