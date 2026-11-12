using Api.Agente.Application.Analise;
using Api.Agente.Application.Tests.Fakes;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Analise;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Tests.Analise;

/// <summary>Validação estrutural e semântica da saída do LLM e retentativas (FR-047, FR-084, research R-10).</summary>
public class ValidadorRespostaLlmTests
{
    private const string AchadoId = "EFD-EST-010-000001";

    private static readonly HashSet<string> Achados = [AchadoId];

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static ContextoRecuperacao Recuperacao()
    {
        var recuperacao = new ContextoRecuperacao();
        recuperacao.Registrar([BaseInformacaoFalsa.TrechoOficial, BaseInformacaoFalsa.TrechoInterno]);
        return recuperacao;
    }

    private static Execucao ExecucaoComAchado()
    {
        var execucao = Execucao.Criar(Guid.NewGuid(), "org", "usuario", "correlacao", CanalExecucao.Rest, DateTimeOffset.UtcNow);
        execucao.DefinirIdentificacao(new IdentificacaoArquivo("020", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), "0", "SP", "12345678000195", "A"));
        execucao.AplicarResultado(
            [new Achado(AchadoId, Categoria.ErroEstrutural, Severidade.Impeditiva, Origem.Validador, "Unidade sem 0190.", "EFD-EST-010",
                registro: "0200", campo: "UNID_INV", fonte: new Fonte("Guia Prático EFD ICMS/IPI", "3.2.4", "Registro 0190"))],
            new Dictionary<string, long> { ["EFD-EST-010"] = 1 },
            ["EFD-EST-010"],
            [],
            []);
        return execucao;
    }

    [Fact]
    public void Resposta_valida_e_aceita_com_referencias_montadas_pelo_sistema()
    {
        var resposta = RespostasLlm.Analise(explicacoes: [RespostasLlm.Explicacao(AchadoId, BaseInformacaoFalsa.TrechoOficial.TrechoId)]);

        var resultado = ValidadorRespostaLlm.ValidarAnalise(resposta, Achados, Recuperacao());
        var analise = ValidadorRespostaLlm.MontarAnaliseAceita(resultado.Conteudo!, Recuperacao(), "modelo", 1);

        Assert.True(resultado.Valida);
        var referencia = Assert.Single(analise.Referencias);
        Assert.Equal("3.2.4", referencia.Versao);
        Assert.Equal(ClassificacaoFonte.Oficial, referencia.Classificacao);
        Assert.True(analise.FundamentadaEmFonteOficial);
    }

    [Fact]
    public void Rejeita_achado_inexistente()
    {
        var resposta = RespostasLlm.Analise(explicacoes: [RespostasLlm.Explicacao("EFD-INVENTADO-1", BaseInformacaoFalsa.TrechoOficial.TrechoId)]);

        var resultado = ValidadorRespostaLlm.ValidarAnalise(resposta, Achados, Recuperacao());

        Assert.False(resultado.Valida);
        Assert.Contains(resultado.Erros, e => e.Contains("EFD-INVENTADO-1", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejeita_trecho_nao_recuperado_na_execucao()
    {
        var inventado = Guid.NewGuid().ToString();
        var resposta = RespostasLlm.Analise(explicacoes: [RespostasLlm.Explicacao(AchadoId, inventado)]);

        var resultado = ValidadorRespostaLlm.ValidarAnalise(resposta, Achados, Recuperacao());

        Assert.False(resultado.Valida);
        Assert.Contains(resultado.Erros, e => e.Contains(inventado, StringComparison.Ordinal) && e.Contains("não foi recuperado", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejeita_indicio_impeditivo()
    {
        var resposta = RespostasLlm.Analise(indicios: [RespostasLlm.Indicio("impeditiva", BaseInformacaoFalsa.TrechoOficial.TrechoId)]);

        var resultado = ValidadorRespostaLlm.ValidarAnalise(resposta, Achados, Recuperacao());

        Assert.False(resultado.Valida);
        Assert.Contains(resultado.Erros, e => e.Contains("nunca é impeditivo", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "achados": [] }""")]
    [InlineData("""{ "status": "concluida" }""")]
    [InlineData("""{ "revisaoHumanaNecessaria": false }""")]
    public void Rejeita_tentativa_de_alterar_achados_ou_metadados(string extra)
    {
        var resposta = RespostasLlm.Analise(extra: System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(extra));

        var resultado = ValidadorRespostaLlm.ValidarAnalise(resposta, Achados, Recuperacao());

        Assert.False(resultado.Valida);
        Assert.Contains(resultado.Erros, e => e.Contains("fora do contrato", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejeita_explicacao_que_tenta_mudar_a_severidade_do_achado()
    {
        var resposta = $$"""
            {"versaoContrato":"1.0.0","resumo":"r","explicacoes":[{"achadoId":"{{AchadoId}}","texto":"t","causaProvavel":"c","acaoCorretiva":"a","trechoIds":[],"severidade":"informativa"}],
             "indicios":[],"limitacoes":[],"revisaoHumanaRecomendada":false}
            """;

        var resultado = ValidadorRespostaLlm.ValidarAnalise(resposta, Achados, Recuperacao());

        Assert.False(resultado.Valida);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"versaoContrato":"1.0.0"}""")]
    [InlineData("""{"versaoContrato":"2.0.0","resumo":"r","explicacoes":[],"indicios":[],"limitacoes":[],"revisaoHumanaRecomendada":false}""")]
    public void Rejeita_estrutura_fora_do_contrato(string resposta)
    {
        Assert.False(ValidadorRespostaLlm.ValidarAnalise(resposta, Achados, Recuperacao()).Valida);
    }

    [Fact]
    public void So_trechos_internos_nao_fundamentam_em_fonte_oficial()
    {
        var resposta = RespostasLlm.Analise(explicacoes: [RespostasLlm.Explicacao(AchadoId, BaseInformacaoFalsa.TrechoInterno.TrechoId)]);

        var resultado = ValidadorRespostaLlm.ValidarAnalise(resposta, Achados, Recuperacao());
        var analise = ValidadorRespostaLlm.MontarAnaliseAceita(resultado.Conteudo!, Recuperacao(), "modelo", 1);

        Assert.False(analise.FundamentadaEmFonteOficial);
        Assert.Contains(Fundamentacao.SemFonteOficial, analise.Conteudo!.Limitacoes);
    }

    [Fact]
    public async Task Apos_duas_retentativas_invalidas_a_analise_falha_e_os_achados_sao_preservados()
    {
        var modelo = new ModeloFalso("inválida 1", RespostasLlm.Analise(explicacoes: [RespostasLlm.Explicacao("X", "Y")]), """{"status":"conforme"}""");
        var analisador = new AnalisadorExecucao(BaseInformacaoFalsa.ComTrechos(), modelo, new ParametrosAnalise(2, 5, 20));
        var execucao = ExecucaoComAchado();
        var recuperacao = new ContextoRecuperacao();
        var limitacoes = await analisador.ConsultarBaseAsync(execucao, recuperacao, Cancelamento);

        var analise = await analisador.AnalisarAsync(execucao, recuperacao, limitacoes, Cancelamento);
        execucao.RegistrarAnaliseIa(analise);

        Assert.Equal(StatusAnaliseIa.Falha, analise.Status);
        Assert.Equal(3, analise.Tentativas);
        Assert.Null(analise.Conteudo);
        Assert.Equal([1, 2, 3], modelo.Pedidos.Select(p => p.Tentativa));
        Assert.Contains(modelo.Pedidos[1].Mensagens, m => m.Texto.Contains("rejeitada pela validação", StringComparison.Ordinal));
        var achado = Assert.Single(execucao.Achados);
        Assert.Equal(Severidade.Impeditiva, achado.Severidade);
    }

    [Fact]
    public async Task Retentativa_valida_e_aceita_e_o_indicio_entra_como_achado_da_ia()
    {
        var modelo = new ModeloFalso(
            "inválida",
            RespostasLlm.Analise(
                explicacoes: [RespostasLlm.Explicacao(AchadoId, BaseInformacaoFalsa.TrechoOficial.TrechoId)],
                indicios: [RespostasLlm.Indicio("advertencia", BaseInformacaoFalsa.TrechoOficial.TrechoId)]));
        var analisador = new AnalisadorExecucao(BaseInformacaoFalsa.ComTrechos(), modelo, new ParametrosAnalise(2, 5, 20));
        var execucao = ExecucaoComAchado();
        var recuperacao = new ContextoRecuperacao();
        await analisador.ConsultarBaseAsync(execucao, recuperacao, Cancelamento);

        var analise = await analisador.AnalisarAsync(execucao, recuperacao, [], Cancelamento);
        execucao.RegistrarAnaliseIa(analise);

        Assert.Equal(StatusAnaliseIa.Aceita, analise.Status);
        Assert.Equal(2, analise.Tentativas);
        var indicio = Assert.Single(execucao.Achados, a => a.Origem == Origem.Ia);
        Assert.Equal(Categoria.IndicioIa, indicio.Categoria);
        Assert.Equal(Severidade.Advertencia, indicio.Severidade);
        Assert.Equal(Severidade.Impeditiva, Assert.Single(execucao.Achados, a => a.Origem == Origem.Validador).Severidade);
    }
}
