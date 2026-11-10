using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Execucoes;
using Api.Agente.Infrastructure.Persistencia.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Api.Agente.Integration.Tests.Persistencia;

public class MapeamentoMongoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static ExecucaoEstado EstadoCompleto()
    {
        var execucao = Execucao.Criar(Guid.NewGuid(), "org", "usuario", "correlacao", CanalExecucao.Rest, Agora);
        execucao.AtualizarEtapa(1, StatusEtapa.Concluida, Agora);
        execucao.RegistrarOcorrencia("EFD-ESTR-001");
        execucao.RegistrarAchado(new Achado(
            "a1", Categoria.ErroEstrutural, Severidade.Impeditiva, Origem.Validador, "Falta K990",
            "EFD-ESTR-001", 10, "K990", fonte: new Fonte("Guia", "3.2.4", "Cap. II", Guid.NewGuid())));
        execucao.RegistrarValidacaoNaoRealizada(new ValidacaoNaoRealizada("etapa-5", Categoria.FalhaTecnica, "base indisponível"));
        execucao.AplicarResultado([], new Dictionary<string, long>(), [], [],
            [new ValidacaoNaoRealizada("EFD-NEG-008", Categoria.AusenciaEvidencias, "Regra pendente de confirmação em fonte oficial.")]);
        return execucao.Exportar(Agora) with
        {
            Arquivo = new ArquivoExecucao("efd.txt", 10, new string('a', 64)),
            Identificacao = new IdentificacaoArquivo("020", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), "0", "SP", "12345678000195", "A"),
        };
    }

    [Fact]
    public void Nomes_e_tipos_seguem_o_modelo_de_dados()
    {
        MongoExtensions.GarantirMapeamento();

        var documento = EstadoCompleto().ToBsonDocument();

        Assert.True(documento.Contains("_id"));
        Assert.Equal("org", documento["orgId"].AsString);
        Assert.Equal("Recebida", documento["estado"].AsString);
        // O índice TTL só funciona com BSON Date.
        Assert.Equal(BsonType.DateTime, documento["expiraEm"].BsonType);
        Assert.Equal("ErroEstrutural", documento["achados"][0]["categoria"].AsString);
        Assert.Equal(1, documento["totaisPorRegra"]["EFD-ESTR-001"].ToInt32());
    }

    [Fact]
    public void Ida_e_volta_preserva_o_estado()
    {
        MongoExtensions.GarantirMapeamento();
        var original = EstadoCompleto();

        var restaurado = BsonSerializer.Deserialize<ExecucaoEstado>(original.ToBsonDocument());
        var execucao = Execucao.Restaurar(restaurado);

        Assert.Equal(original.Id, restaurado.Id);
        Assert.Equal(original.ExpiraEm, restaurado.ExpiraEm);
        Assert.Equal(original.Identificacao, restaurado.Identificacao);
        Assert.Equal(9, execucao.Etapas.Count);
        Assert.Equal(StatusEtapa.Concluida, execucao.Etapas[0].Status);
        Assert.Equal(1, execucao.TotaisPorRegra["EFD-ESTR-001"]);
        var achado = Assert.Single(execucao.Achados);
        Assert.Equal("K990", achado.Registro);
        Assert.Equal(original.Achados[0].Fonte, achado.Fonte);
        Assert.Equal("base indisponível", Assert.Single(execucao.ValidacoesNaoRealizadas).Motivo);
        Assert.Equal("EFD-NEG-008", Assert.Single(execucao.ForaDoAlcance).Referencia);
    }

    [Fact]
    public void Lista_fora_do_alcance_e_gravada_com_o_nome_do_modelo_de_dados()
    {
        MongoExtensions.GarantirMapeamento();

        var documento = EstadoCompleto().ToBsonDocument();

        Assert.Equal("EFD-NEG-008", documento["foraDoAlcance"][0]["referencia"].AsString);
        Assert.Equal("AusenciaEvidencias", documento["foraDoAlcance"][0]["categoria"].AsString);
    }

    [Fact]
    public void Documento_gravado_antes_da_lista_e_lido_com_lista_vazia()
    {
        MongoExtensions.GarantirMapeamento();
        var documento = EstadoCompleto().ToBsonDocument();
        documento.Remove("foraDoAlcance");

        var execucao = Execucao.Restaurar(BsonSerializer.Deserialize<ExecucaoEstado>(documento));

        Assert.Empty(execucao.ForaDoAlcance);
        Assert.Equal("base indisponível", Assert.Single(execucao.ValidacoesNaoRealizadas).Motivo);
    }

    [Fact]
    public void Documento_nao_tem_campo_de_conteudo_do_arquivo()
    {
        MongoExtensions.GarantirMapeamento();

        var arquivo = EstadoCompleto().ToBsonDocument()["arquivo"].AsBsonDocument;

        Assert.Equal(["nome", "tamanhoBytes", "sha256"], arquivo.Names);
    }
}
