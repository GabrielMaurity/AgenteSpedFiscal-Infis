using Api.Agente.Application.Analise;
using Api.Agente.Application.Consumo;
using Api.Agente.Application.Tests.Fakes;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Infrastructure.Llm;
using Api.Agente.Infrastructure.Llm.Ferramentas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Api.Agente.Application.Tests.Ferramentas;

/// <summary>Ferramentas governadas pelo sistema, não pelo modelo (P-15, research R-11).</summary>
public class FiltroAutorizacaoTests
{
    private static readonly DadosEfd Dados = DadosEfd.Carregar();

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private sealed class RegistradorMemoria : IRegistradorConsumo
    {
        public List<RegistroConsumo> Registros { get; } = [];

        public Task RegistrarAsync(RegistroConsumo registro, CancellationToken cancelamento)
        {
            Registros.Add(registro);
            return Task.CompletedTask;
        }
    }

    private static SessaoFerramentas Sessao(TipoSessao tipo = TipoSessao.Conversa, bool temExecucao = false, params string[] papeis) => new(
        tipo,
        Guid.NewGuid(),
        "org",
        "correlacao",
        papeis.Length > 0 ? papeis : ["analista"],
        new ContextoRecuperacao(),
        [new Achado("A-1", Categoria.AusenciaEvidencias, Severidade.Informativa, Origem.Validador, "achado")],
        "2026-01",
        temExecucao);

    private static KernelFactory Fabrica(IChatCompletionService chat, BaseInformacaoFalsa? baseInformacao = null, int limite = 8) => new(
        chat,
        baseInformacao ?? BaseInformacaoFalsa.ComTrechos(),
        Dados,
        Options.Create(new OpenAiOpcoes { ApiKey = "x", ModeloAnalise = "modelo-falso" }),
        Options.Create(new LimitesOpcoes { ChamadasFerramentaPorExecucao = limite }),
        Options.Create(new BaseInformacaoOpcoes { TimeoutFerramentaSegundos = 5 }),
        NullLoggerFactory.Instance);

    private static Task<FunctionResult> Invocar(Kernel kernel, string ferramenta, KernelArguments? argumentos = null) =>
        kernel.InvokeAsync(KernelFactory.NomePlugin, ferramenta, argumentos ?? [], Cancelamento);

    [Fact]
    public async Task Ferramenta_fora_da_lista_e_negada_e_nao_executa()
    {
        var sessao = Sessao(TipoSessao.Conversa, temExecucao: false);
        var kernel = Fabrica(FakeChatCompletionService.ComMensagens()).Criar(sessao);

        var resultado = await Invocar(kernel, FerramentasAgente.ObterAchados);

        Assert.StartsWith(FiltroAutorizacaoFerramentas.PrefixoNegacao, resultado.GetValue<string>(), StringComparison.Ordinal);
        var chamada = Assert.Single(sessao.Chamadas);
        Assert.False(chamada.Autorizada);
        Assert.Contains("não está autorizada", chamada.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ferramenta_escolhida_pelo_modelo_tambem_passa_pelo_filtro()
    {
        var pedidoDeFerramenta = new ChatMessageContent(AuthorRole.Assistant, content: null);
        pedidoDeFerramenta.Items.Add(new FunctionCallContent(FerramentasAgente.ObterAchados, KernelFactory.NomePlugin, "chamada-1"));
        var chat = FakeChatCompletionService.ComMensagens(pedidoDeFerramenta, new ChatMessageContent(AuthorRole.Assistant, "{}"));
        var registrador = new RegistradorMemoria();
        var escopos = new ServiceCollection().AddSingleton<IRegistradorConsumo>(registrador).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var modelo = new ModeloAnaliseSemanticKernel(Fabrica(chat), escopos, TimeProvider.System, NullLogger<ModeloAnaliseSemanticKernel>.Instance);
        var sessao = Sessao(TipoSessao.Conversa, temExecucao: false);

        var resposta = await modelo.GerarAsync(new PedidoModelo("instruções", [new MensagemModelo(true, "pergunta")], FormatoResposta.Conversa, sessao, 1), Cancelamento);

        Assert.Equal("{}", resposta.Conteudo);
        Assert.False(Assert.Single(sessao.Chamadas).Autorizada);
        var resultadoFerramenta = chat.Historicos[1].SelectMany(m => m.Items).OfType<FunctionResultContent>().Single();
        Assert.StartsWith(FiltroAutorizacaoFerramentas.PrefixoNegacao, resultadoFerramenta.Result?.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, registrador.Registros.Count);
        Assert.All(registrador.Registros, r => Assert.Equal(1, r.Tentativa));
    }

    [Fact]
    public async Task Ferramenta_inexistente_pedida_pelo_modelo_e_negada()
    {
        var pedidoDeFerramenta = new ChatMessageContent(AuthorRole.Assistant, content: null);
        pedidoDeFerramenta.Items.Add(new FunctionCallContent("excluir_execucao", KernelFactory.NomePlugin, "chamada-1"));
        var chat = FakeChatCompletionService.ComMensagens(pedidoDeFerramenta, new ChatMessageContent(AuthorRole.Assistant, "{}"));
        var escopos = new ServiceCollection().AddSingleton<IRegistradorConsumo>(new RegistradorMemoria()).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var modelo = new ModeloAnaliseSemanticKernel(Fabrica(chat), escopos, TimeProvider.System, NullLogger<ModeloAnaliseSemanticKernel>.Instance);
        var sessao = Sessao(TipoSessao.Validacao, temExecucao: true, SessaoFerramentas.PapelSistema);

        await modelo.GerarAsync(new PedidoModelo("instruções", [new MensagemModelo(true, "analise")], FormatoResposta.Analise, sessao, 1), Cancelamento);

        var chamada = Assert.Single(sessao.Chamadas);
        Assert.Equal("excluir_execucao", chamada.Ferramenta);
        Assert.False(chamada.Autorizada);
    }

    [Fact]
    public async Task Papel_sem_permissao_e_negado()
    {
        var sessao = Sessao(TipoSessao.Conversa, temExecucao: false, "admin_base");
        var kernel = Fabrica(FakeChatCompletionService.ComMensagens()).Criar(sessao);

        var resultado = await Invocar(kernel, FerramentasAgente.ConsultarRegra, new() { ["regraId"] = "EFD-NEG-001" });

        Assert.StartsWith(FiltroAutorizacaoFerramentas.PrefixoNegacao, resultado.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(Assert.Single(sessao.Chamadas).Autorizada);
    }

    [Theory]
    [InlineData(FerramentasAgente.ConsultarManual, "consulta", "")]
    [InlineData(FerramentasAgente.ConsultarManual, "periodo", "2026-13")]
    [InlineData(FerramentasAgente.ObterTrecho, "trechoId", "não-é-guid")]
    [InlineData(FerramentasAgente.ConsultarRegra, "regraId", "'; DROP TABLE regras; --")]
    [InlineData(FerramentasAgente.ObterAchados, "pagina", "0")]
    public async Task Argumentos_invalidos_sao_rejeitados(string ferramenta, string argumento, string valor)
    {
        var sessao = Sessao(TipoSessao.Validacao, temExecucao: true, SessaoFerramentas.PapelSistema);
        var kernel = Fabrica(FakeChatCompletionService.ComMensagens()).Criar(sessao);
        var argumentos = new KernelArguments { [argumento] = valor };
        if (ferramenta == FerramentasAgente.ConsultarManual && argumento != "consulta")
        {
            argumentos["consulta"] = "C190";
        }

        var resultado = await Invocar(kernel, ferramenta, argumentos);

        Assert.StartsWith(FiltroAutorizacaoFerramentas.PrefixoNegacao, resultado.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(Assert.Single(sessao.Chamadas).Autorizada);
    }

    [Fact]
    public async Task Limite_de_oito_chamadas_por_execucao()
    {
        var sessao = Sessao(TipoSessao.Validacao, temExecucao: true, SessaoFerramentas.PapelSistema);
        var kernel = Fabrica(FakeChatCompletionService.ComMensagens(), limite: 8).Criar(sessao);

        for (var i = 0; i < 9; i++)
        {
            await Invocar(kernel, FerramentasAgente.ConsultarRegra, new() { ["regraId"] = "EFD-NEG-001" });
        }

        Assert.Equal(9, sessao.Chamadas.Count);
        Assert.All(sessao.Chamadas.Take(8), c => Assert.True(c.Autorizada));
        Assert.False(sessao.Chamadas[8].Autorizada);
        Assert.Contains("Limite de 8", sessao.Chamadas[8].Motivo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chamada_autorizada_executa_e_registra_os_trechos_recuperados()
    {
        var sessao = Sessao(TipoSessao.Conversa, temExecucao: false);
        var kernel = Fabrica(FakeChatCompletionService.ComMensagens()).Criar(sessao);

        var resultado = await Invocar(kernel, FerramentasAgente.ConsultarManual, new() { ["consulta"] = "C190" });

        Assert.Contains(BaseInformacaoFalsa.TrechoOficial.TrechoId, resultado.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(Assert.Single(sessao.Chamadas).Autorizada);
        Assert.True(sessao.Recuperacao.Contem(BaseInformacaoFalsa.TrechoOficial.TrechoId));
    }

    [Fact]
    public async Task Base_indisponivel_volta_ao_modelo_como_erro_sem_detalhes()
    {
        var sessao = Sessao(TipoSessao.Conversa, temExecucao: false);
        var kernel = Fabrica(FakeChatCompletionService.ComMensagens(), new BaseInformacaoFalsa()).Criar(sessao);

        var resultado = await Invocar(kernel, FerramentasAgente.ConsultarManual, new() { ["consulta"] = "C190" });

        Assert.Equal("Erro: base de informação indisponível.", resultado.GetValue<string>());
        Assert.Equal("base de informação indisponível", Assert.Single(sessao.Chamadas).Motivo);
    }
}
