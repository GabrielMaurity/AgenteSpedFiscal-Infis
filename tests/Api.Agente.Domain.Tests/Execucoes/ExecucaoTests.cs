using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Domain.Tests.Execucoes;

public class ExecucaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static Execucao Nova() =>
        Execucao.Criar(Guid.NewGuid(), "org", "usuario", "correlacao", CanalExecucao.Rest, Agora);

    private static Achado Advertencia() =>
        new("a1", Categoria.InconsistenciaNegocio, Severidade.Advertencia, Origem.Validador, "lacuna",
            "EFD-NEG-001", fonte: new Fonte("Guia", "3.2.4", "Cap. II"));

    [Fact]
    public void Criacao_tem_nove_etapas_e_expira_em_90_dias()
    {
        var execucao = Nova();

        Assert.Equal(9, execucao.Etapas.Count);
        Assert.All(execucao.Etapas, e => Assert.Equal(StatusEtapa.Pendente, e.Status));
        Assert.Equal(Agora.AddDays(90), execucao.ExpiraEm);
        Assert.Equal(EstadoExecucao.Recebida, execucao.Estado);
    }

    [Fact]
    public void Segue_a_sequencia_de_estados_do_modelo_de_dados()
    {
        var execucao = Nova();

        execucao.AvancarPara(EstadoExecucao.ValidandoArquivo);
        execucao.AvancarPara(EstadoExecucao.Identificando);
        execucao.AvancarPara(EstadoExecucao.Validando);
        execucao.AvancarPara(EstadoExecucao.ConsultandoBase);
        execucao.AvancarPara(EstadoExecucao.AnalisandoIa);
        execucao.AvancarPara(EstadoExecucao.ValidandoSaida);

        Assert.Equal(EstadoExecucao.ValidandoSaida, execucao.Estado);
    }

    [Theory]
    [InlineData(EstadoExecucao.Validando)]
    [InlineData(EstadoExecucao.Recebida)]
    [InlineData(EstadoExecucao.Concluida)]
    public void Rejeita_transicao_fora_da_sequencia(EstadoExecucao alvo)
    {
        Assert.Throws<InvalidOperationException>(() => Nova().AvancarPara(alvo));
    }

    [Fact]
    public void Concluida_exige_analise_aceita_e_nenhuma_pendencia()
    {
        var execucao = Nova();

        execucao.Concluir(analiseIaAceita: true);

        Assert.Equal(EstadoExecucao.Concluida, execucao.Estado);
    }

    [Fact]
    public void Sem_analise_da_ia_fica_concluida_com_limitacoes()
    {
        var execucao = Nova();

        execucao.Concluir(analiseIaAceita: false);

        Assert.Equal(EstadoExecucao.ConcluidaComLimitacoes, execucao.Estado);
    }

    [Fact]
    public void Validacao_nao_realizada_impede_concluida_mesmo_com_analise_aceita()
    {
        var execucao = Nova();
        execucao.RegistrarValidacaoNaoRealizada(new ValidacaoNaoRealizada("EFD-NEG-003", Categoria.FalhaTecnica, "tabela ausente"));

        execucao.Concluir(analiseIaAceita: true);

        Assert.Equal(EstadoExecucao.ConcluidaComLimitacoes, execucao.Estado);
    }

    [Fact]
    public void Qualquer_estado_pode_ir_para_falha_com_motivo()
    {
        var execucao = Nova();
        execucao.AvancarPara(EstadoExecucao.ValidandoArquivo);

        execucao.Falhar("arquivo truncado");

        Assert.Equal(EstadoExecucao.Falha, execucao.Estado);
        Assert.Equal("arquivo truncado", execucao.MotivoFalha);
    }

    [Fact]
    public void Execucao_finalizada_nao_muda_mais()
    {
        var execucao = Nova();
        execucao.Concluir(analiseIaAceita: false);

        Assert.Throws<InvalidOperationException>(() => execucao.Falhar("tarde demais"));
        Assert.Throws<InvalidOperationException>(() => execucao.RegistrarAchado(Advertencia()));
    }

    [Fact]
    public void Execucao_limpa_nao_exige_revisao_humana()
    {
        var execucao = Nova();
        execucao.Concluir(analiseIaAceita: true);

        Assert.False(execucao.RevisaoHumanaNecessaria);
    }

    [Fact]
    public void Advertencia_exige_revisao_humana()
    {
        var execucao = Nova();
        execucao.RegistrarAchado(Advertencia());

        Assert.True(execucao.RevisaoHumanaNecessaria);
    }

    [Fact]
    public void Validacao_nao_realizada_exige_revisao_humana()
    {
        var execucao = Nova();
        execucao.RegistrarValidacaoNaoRealizada(new ValidacaoNaoRealizada("etapa-5", Categoria.FalhaTecnica, "base indisponível"));

        Assert.True(execucao.RevisaoHumanaNecessaria);
    }

    [Fact]
    public void Falha_exige_revisao_humana()
    {
        var execucao = Nova();
        execucao.Falhar("erro");

        Assert.True(execucao.RevisaoHumanaNecessaria);
    }

    [Fact]
    public void Totais_contam_alem_dos_achados_detalhados()
    {
        var execucao = Nova();
        execucao.RegistrarOcorrencia("EFD-ESTR-001");
        execucao.RegistrarOcorrencia("EFD-ESTR-001");

        Assert.Equal(2, execucao.TotaisPorRegra["EFD-ESTR-001"]);
        Assert.Empty(execucao.Achados);
    }

    [Fact]
    public void Exportar_e_restaurar_preserva_estado_e_motivo()
    {
        var execucao = Nova();
        execucao.RegistrarAchado(Advertencia());
        execucao.Falhar("erro");

        var restaurada = Execucao.Restaurar(execucao.Exportar(Agora.AddMinutes(1)));

        Assert.Equal(EstadoExecucao.Falha, restaurada.Estado);
        Assert.Equal("erro", restaurada.MotivoFalha);
        Assert.Single(restaurada.Achados);
    }

    [Fact]
    public void Etapa_nao_executada_registra_motivo()
    {
        var execucao = Nova();

        execucao.AtualizarEtapa(5, StatusEtapa.NaoExecutada, Agora, "base indisponível");

        Assert.Equal(StatusEtapa.NaoExecutada, execucao.Etapas[4].Status);
        Assert.Equal("base indisponível", execucao.Etapas[4].Motivo);
        Assert.Null(execucao.Etapas[4].Inicio);
    }
}
