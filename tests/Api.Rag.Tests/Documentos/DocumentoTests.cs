using Api.Rag.Domain.Documentos;

namespace Api.Rag.Tests.Documentos;

public class DocumentoTests
{
    private static readonly string Sha = new('a', 64);

    private static Documento Novo(ClassificacaoDocumento classificacao = ClassificacaoDocumento.Oficial, string tipo = TiposDocumento.GuiaPratico, string versao = "3.2.4") =>
        Documento.Criar(Guid.NewGuid(), classificacao, tipo, "Guia", "SPED", versao, new DateOnly(2026, 1, 1), null, Sha, DateTimeOffset.UtcNow, "admin");

    [Fact]
    public void Documento_novo_fica_recebido()
    {
        Assert.Equal(StatusDocumento.Recebido, Novo().Status);
    }

    [Theory]
    [InlineData(ClassificacaoDocumento.Interno, TiposDocumento.GuiaPratico)]
    [InlineData(ClassificacaoDocumento.Oficial, TiposDocumento.MaterialInterno)]
    public void Classificacao_incompativel_com_o_tipo_e_recusada(ClassificacaoDocumento classificacao, string tipo)
    {
        Assert.Throws<DocumentoInvalidoException>(() => Novo(classificacao, tipo));
    }

    [Fact]
    public void Versao_ausente_e_recusada()
    {
        Assert.Throws<DocumentoInvalidoException>(() => Novo(versao: " "));
    }

    [Fact]
    public void Segue_os_estados_do_processamento()
    {
        var documento = Novo();

        documento.IniciarProcessamento();
        documento.Ativar("modelo", 1536);

        Assert.Equal(StatusDocumento.Ativo, documento.Status);
        Assert.Equal("modelo", documento.ModeloEmbedding);
    }

    [Fact]
    public void Falha_pode_ser_reprocessada_e_limpa_o_motivo()
    {
        var documento = Novo();
        documento.IniciarProcessamento();
        documento.Falhar("PDF sem texto");

        documento.IniciarProcessamento();

        Assert.Equal(StatusDocumento.Processando, documento.Status);
        Assert.Null(documento.MotivoFalha);
    }

    [Fact]
    public void Documento_inativo_nao_volta_a_ser_processado()
    {
        var documento = Novo();
        documento.IniciarProcessamento();
        documento.Ativar("modelo", 1536);
        documento.Desativar();

        Assert.Throws<TransicaoInvalidaException>(documento.IniciarProcessamento);
    }

    [Fact]
    public void Documento_recebido_nao_pode_ser_ativado_sem_processar()
    {
        Assert.Throws<TransicaoInvalidaException>(() => Novo().Ativar("modelo", 1536));
    }

    [Theory]
    [InlineData("2026-03-01", "2026-03-31", true)]
    [InlineData("2025-03-01", "2025-03-31", false)]
    public void Vigencia_cobre_o_periodo(string inicio, string fim, bool vigente)
    {
        var documento = Novo();

        Assert.Equal(vigente, documento.VigenteEm(DateOnly.Parse(inicio, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(fim, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
