namespace Api.Agente.Application.Validacao;

public enum MotivoRecusaArquivo
{
    TamanhoExcedido,
    FormatoInvalido,
}

public sealed class ArquivoRecusadoException(MotivoRecusaArquivo motivo, string mensagem) : Exception(mensagem)
{
    public MotivoRecusaArquivo Motivo { get; } = motivo;
}

public sealed class LimiteExecucoesException() : Exception("A organização atingiu o limite de execuções simultâneas.");

public sealed record ArquivoRecebido(string Caminho, string Nome, long TamanhoBytes, string Sha256);

/// <summary>Volume temporário do arquivo. O conteúdo é apagado ao fim da execução (FR-082).</summary>
public interface IArmazenamentoTemporario
{
    /// <summary>Grava em streaming e recusa tamanho, extensão ou conteúdo não aceitos (FR-002).</summary>
    Task<ArquivoRecebido> SalvarAsync(Stream conteudo, string nome, CancellationToken cancelamento);

    Stream AbrirLeitura(string caminho);

    void Excluir(string caminho);
}

public sealed record ItemFila(Guid ExecucaoId, string OrgId, string Caminho);

public interface IFilaExecucoes
{
    ValueTask EnfileirarAsync(ItemFila item, CancellationToken cancelamento);
}

public sealed record ParametrosValidacao(int OcorrenciasDetalhadasPorRegra);
