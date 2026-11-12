using System.Globalization;
using Api.Agente.Application.Analise;
using Api.Agente.Application.Execucoes;
using Api.Agente.Application.Validacao;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Analise;
using Api.Agente.Domain.Conversas;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Conversas;

/// <summary>Persistência de conversas, sempre filtrada por organização e usuário (FR-054).</summary>
public interface IRepositorioConversas
{
    Task SalvarAsync(Conversa conversa, CancellationToken cancelamento);

    Task<Conversa?> ObterAsync(string orgId, string usuarioId, Guid id, CancellationToken cancelamento);
}

public sealed class ConversaNaoEncontradaException() : Exception("Conversa não encontrada.");

public sealed class ExecucaoNaoEncontradaException() : Exception("Execução não encontrada.");

public sealed class MensagemInvalidaException(string mensagem) : Exception(mensagem);

/// <param name="Papeis">Papéis do usuário, verificados pelo filtro de ferramentas (P-15).</param>
public sealed record ComandoMensagem(
    string OrgId,
    string UsuarioId,
    IReadOnlyCollection<string> Papeis,
    Guid ConversaId,
    string Texto,
    string? AchadoId,
    string? Periodo,
    string CorrelacaoId);

/// <param name="Proximidade">De 0 a 1, calculada pelo sistema a partir da similaridade dos trechos (research R-38).</param>
public sealed record RespostaMensagem(
    string MensagemId,
    string Resposta,
    bool Fundamentada,
    bool FundamentadaEmFonteOficial,
    double Proximidade,
    IReadOnlyList<ReferenciaEnvelope> Referencias,
    IReadOnlyList<string> Limitacoes);

/// <summary>
/// Perguntas e explicações de achados (FR-050 a FR-054). REST e A2A usam este mesmo caso de uso (FR-081).
/// A fundamentação, as referências e as limitações padrão são decididas pelo sistema, não pelo modelo.
/// </summary>
public sealed class ConversasHandler(
    IRepositorioConversas conversas,
    IRepositorioExecucoes execucoes,
    IBaseInformacao baseInformacao,
    IModeloAnalise modelo,
    ParametrosAnalise parametros,
    TimeProvider relogio)
{
    public const int TamanhoMaximoTexto = 4_000;

    public const string ArquivoDescartado =
        "O arquivo foi descartado ao fim do processamento (FR-082) e o dado pedido não está nos achados. Para consultá-lo, envie o arquivo novamente.";

    public const string SemEvidencia = "Não há evidência suficiente na base de informação para responder com fundamento.";
    public const string BaseIndisponivel = "A base de informação está indisponível; não é possível responder com fundamento agora.";
    public const string ModeloIndisponivel = "A IA está indisponível no momento; tente novamente mais tarde.";
    public const string RespostaInvalida = "A resposta da IA não passou na validação do sistema; reformule a pergunta ou tente novamente.";
    public const string RespostaSemFundamento = "Não foi possível responder com fundamento.";

    public async Task<Conversa> CriarAsync(string orgId, string usuarioId, Guid? execucaoId, CancellationToken cancelamento)
    {
        if (execucaoId is { } id && await execucoes.ObterAsync(orgId, id, cancelamento) is null)
        {
            throw new ExecucaoNaoEncontradaException();
        }

        var conversa = Conversa.Criar(Guid.CreateVersion7(), orgId, usuarioId, execucaoId, relogio.GetUtcNow());
        await conversas.SalvarAsync(conversa, cancelamento);
        return conversa;
    }

    public async Task<IReadOnlyList<MensagemConversa>> ListarAsync(string orgId, string usuarioId, Guid conversaId, CancellationToken cancelamento) =>
        (await conversas.ObterAsync(orgId, usuarioId, conversaId, cancelamento) ?? throw new ConversaNaoEncontradaException()).Mensagens;

    public async Task<RespostaMensagem> EnviarAsync(ComandoMensagem comando, CancellationToken cancelamento)
    {
        if (string.IsNullOrWhiteSpace(comando.Texto) || comando.Texto.Length > TamanhoMaximoTexto)
        {
            throw new MensagemInvalidaException($"texto deve ter de 1 a {TamanhoMaximoTexto} caracteres.");
        }

        if (comando.Periodo is not null && !PeriodoValido(comando.Periodo))
        {
            throw new MensagemInvalidaException("periodo deve estar no formato AAAA-MM.");
        }

        var conversa = await conversas.ObterAsync(comando.OrgId, comando.UsuarioId, comando.ConversaId, cancelamento)
            ?? throw new ConversaNaoEncontradaException();

        // A execução pode ter expirado (FR-082): a conversa segue sem os achados.
        var execucao = conversa.ExecucaoId is { } execucaoId ? await execucoes.ObterAsync(comando.OrgId, execucaoId, cancelamento) : null;
        Achado? achado = null;
        if (comando.AchadoId is not null)
        {
            achado = execucao?.Achados.FirstOrDefault(a => a.Id == comando.AchadoId)
                ?? throw new MensagemInvalidaException("achadoId não pertence à execução vinculada à conversa.");
        }

        var historico = conversa.Mensagens.ToList();
        var agora = relogio.GetUtcNow();
        conversa.Adicionar(new MensagemConversa(Guid.CreateVersion7(), PapelMensagem.Usuario, comando.Texto, [], false, false, [], agora, comando.AchadoId, comando.Periodo));

        var resposta = await ResponderAsync(comando, conversa, historico, execucao, achado, cancelamento);
        conversa.Adicionar(resposta);
        await conversas.SalvarAsync(conversa, cancelamento);

        return new RespostaMensagem(
            resposta.Id.ToString(),
            resposta.Texto,
            resposta.Fundamentada,
            resposta.FundamentadaEmFonteOficial,
            resposta.Proximidade ?? 0,
            resposta.Referencias.Select(EnvelopeMapper.MapearReferencia).ToList(),
            resposta.Limitacoes);
    }

    private async Task<MensagemConversa> ResponderAsync(
        ComandoMensagem comando,
        Conversa conversa,
        IReadOnlyList<MensagemConversa> historico,
        Execucao? execucao,
        Achado? achado,
        CancellationToken cancelamento)
    {
        var periodo = comando.Periodo
            ?? execucao?.Identificacao?.PeriodoInicio.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            ?? relogio.GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var recuperacao = new ContextoRecuperacao();
        try
        {
            var consulta = achado is null ? comando.Texto : $"{achado.Registro} {achado.Campo} {achado.Mensagem} {comando.Texto}";
            await baseInformacao.BuscarAsync(
                new ConsultaBase(Cortar(consulta), comando.CorrelacaoId, Periodo: periodo, TopK: parametros.TopK), recuperacao, cancelamento);
        }
        catch (Exception excecao) when (excecao is BaseIndisponivelException or ConsultaInvalidaException)
        {
            // Sem base não há fundamento: o modelo não é chamado (SC-004).
            return SemFundamento(BaseIndisponivel, 0);
        }

        var sessao = new SessaoFerramentas(
            TipoSessao.Conversa,
            conversa.Id,
            comando.OrgId,
            comando.CorrelacaoId,
            comando.Papeis,
            recuperacao,
            execucao is null ? [] : [.. execucao.Achados],
            periodo,
            temExecucao: execucao is not null);

        var prompt = ConstrutorPrompt.ParaConversa(historico, comando.Texto, achado, execucao, recuperacao.Trechos, parametros.MensagensEnviadasAoModelo);
        var limite = parametros.MensagensEnviadasAoModelo;
        var correcao = new List<MensagemModelo>();
        for (var tentativa = 1; tentativa <= parametros.RetentativasResposta + 1; tentativa++)
        {
            // A pergunta atual é a última do prompt; a retentativa leva só a última correção, sem passar do limite.
            var extras = correcao.TakeLast(Math.Max(0, limite - 1)).ToList();
            var mensagens = prompt.Mensagens.TakeLast(limite - extras.Count).Concat(extras).ToList();

            RespostaModelo bruta;
            try
            {
                bruta = await modelo.GerarAsync(new PedidoModelo(prompt.InstrucoesSistema, mensagens, FormatoResposta.Conversa, sessao, tentativa), cancelamento);
            }
            catch (ModeloIndisponivelException)
            {
                return SemFundamento(ModeloIndisponivel, Proximidade([], recuperacao));
            }

            var validacao = ValidadorRespostaLlm.ValidarConversa(bruta.Conteudo, recuperacao);
            if (validacao.Valida)
            {
                return Montar(validacao.Conteudo!, recuperacao, execucao is not null);
            }

            correcao = [new MensagemModelo(false, bruta.Conteudo), ConstrutorPrompt.Correcao(validacao.Erros)];
        }

        return SemFundamento(RespostaInvalida, Proximidade([], recuperacao));
    }

    /// <param name="temExecucao">Só conversa vinculada a execução tem arquivo descartado a citar (research R-29, R-38).</param>
    private MensagemConversa Montar(RespostaConversaLlm resposta, ContextoRecuperacao recuperacao, bool temExecucao)
    {
        var referencias = ValidadorRespostaLlm.Referencias(resposta.TrechoIds, recuperacao);
        var fundamentada = resposta.Fundamentada && !resposta.DependeDoArquivo && referencias.Count > 0;
        var limitacoes = Fundamentacao.Limitacoes(resposta.Limitacoes, referencias);
        if (resposta.DependeDoArquivo && temExecucao)
        {
            limitacoes.Add(ArquivoDescartado);
        }
        else if (!fundamentada && !limitacoes.Contains(SemEvidencia))
        {
            limitacoes.Add(SemEvidencia);
        }

        return new MensagemConversa(
            Guid.CreateVersion7(),
            PapelMensagem.Agente,
            resposta.Resposta,
            referencias,
            fundamentada,
            fundamentada && Fundamentacao.EmFonteOficial(referencias),
            limitacoes,
            relogio.GetUtcNow(),
            Proximidade: Proximidade(referencias, recuperacao));
    }

    private MensagemConversa SemFundamento(string limitacao, double proximidade) =>
        new(Guid.CreateVersion7(), PapelMensagem.Agente, RespostaSemFundamento, [], false, false, [limitacao], relogio.GetUtcNow(), Proximidade: proximidade);

    /// <summary>
    /// Maior similaridade entre a pergunta e os trechos citados; sem citação, entre os recuperados; 0 sem
    /// trechos. Calculada pelo sistema, nunca pelo modelo (P-12, research R-38).
    /// </summary>
    public static double Proximidade(IReadOnlyCollection<ReferenciaTrecho> citadas, ContextoRecuperacao recuperacao)
    {
        var base_ = citadas.Count > 0
            ? citadas.Select(r => recuperacao.Obter(r.TrechoId)).OfType<TrechoBase>()
            : recuperacao.Trechos;
        return Math.Round(Math.Clamp(base_.Select(t => t.Similaridade ?? 0).DefaultIfEmpty(0).Max(), 0, 1), 4);
    }

    private static string Cortar(string consulta) => consulta.Length <= 1_000 ? consulta : consulta[..1_000];

    private static bool PeriodoValido(string periodo) =>
        periodo.Length == 7
        && periodo[4] == '-'
        && periodo[..4].All(char.IsAsciiDigit)
        && int.TryParse(periodo.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var mes)
        && mes is >= 1 and <= 12;
}
