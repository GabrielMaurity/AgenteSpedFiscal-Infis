using System.Net;
using System.Text;
using System.Text.Json;
using Api.Rag.Application.Consumo;
using Api.Rag.Infrastructure.Busca;
using Api.Rag.Infrastructure.Configuracao;
using Api.Rag.Infrastructure.Embeddings;
using Api.Rag.Infrastructure.Extracao;
using Api.Rag.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Api.Rag.Tests.Extracao;

public class ExtracaoEEmbeddingsTests
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public void Pdf_preserva_paginas_e_ordem_das_linhas()
    {
        var pdf = PdfAmostra.Gerar([["REGISTRO C190: REGISTRO ANALITICO", "01 REG Texto fixo"], ["Campo 05 (VL_OPR)"]]);

        var paginas = ExtratorPdf.Extrair(pdf);

        Assert.Equal([1, 2], paginas.Select(p => p.Numero));
        Assert.Equal(["REGISTRO C190: REGISTRO ANALITICO", "01 REG Texto fixo"], paginas[0].Linhas);
        Assert.Equal("Campo 05 (VL_OPR)", Assert.Single(paginas[1].Linhas));
    }

    [Theory]
    [InlineData("o que entra no VL_OPR do C190?", new[] { "VL_OPR", "C190" })]
    [InlineData("registro 0200 e campo COD_NCM", new[] { "0200", "COD_NCM" })]
    [InlineData("quando informar o registro?", new string[0])]
    public void Busca_reconhece_codigos_de_registro_e_de_campo(string consulta, string[] esperados)
    {
        Assert.Equal(esperados, BuscaHibrida.Codigos(consulta));
    }

    private sealed class RegistradorFalso : IRegistradorConsumoEmbeddings
    {
        public List<RegistroConsumoEmbeddings> Registros { get; } = [];

        public Task RegistrarAsync(RegistroConsumoEmbeddings registro, CancellationToken cancelamento)
        {
            Registros.Add(registro);
            return Task.CompletedTask;
        }
    }

    private sealed class ServidorFalso(params HttpResponseMessage[] respostas) : HttpMessageHandler
    {
        private int _indice;

        public List<string> Corpos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken cancelamento)
        {
            Corpos.Add(await requisicao.Content!.ReadAsStringAsync(cancelamento));
            return respostas[_indice++];
        }
    }

    private static HttpResponseMessage Sucesso(int textos, int? tokens = 10, int dimensao = 1536)
    {
        var dados = Enumerable.Range(0, textos).Select(i => new { index = i, embedding = new float[dimensao] });
        object corpo = tokens is null ? new { data = dados } : new { data = dados, usage = new { prompt_tokens = tokens } };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json") };
    }

    private static (GeradorEmbeddings Gerador, RegistradorFalso Registrador, ServidorFalso Servidor) Montar(params HttpResponseMessage[] respostas)
    {
        var servidor = new ServidorFalso(respostas);
        var registrador = new RegistradorFalso();
        var http = new HttpClient(servidor) { BaseAddress = new Uri("https://openai.teste/v1/") };
        var opcoes = Options.Create(new OpenAiOpcoes { ApiKey = "x", Retentativas = 2 });
        return (new GeradorEmbeddings(http, opcoes, registrador, TimeProvider.System, NullLogger<GeradorEmbeddings>.Instance), registrador, servidor);
    }

    [Fact]
    public async Task Embeddings_registram_o_consumo_informado_pela_api()
    {
        var (gerador, registrador, servidor) = Montar(Sucesso(2, tokens: 42));

        var vetores = await gerador.GerarAsync(["a", "b"], null, "corr", Cancelamento);

        Assert.Equal(2, vetores.Count);
        var registro = Assert.Single(registrador.Registros);
        Assert.Equal((42, 1, "sucesso", "corr"), (registro.TokensEntrada!.Value, registro.Tentativa, registro.Status, registro.CorrelacaoId));
        Assert.Contains("\"model\":\"text-embedding-3-small\"", servidor.Corpos[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retentativa_gera_um_registro_por_tentativa()
    {
        var (gerador, registrador, _) = Montar(new HttpResponseMessage(HttpStatusCode.TooManyRequests), Sucesso(1));

        await gerador.GerarAsync(["a"], null, "corr", Cancelamento);

        Assert.Equal([1, 2], registrador.Registros.Select(r => r.Tentativa));
        Assert.Equal(["erro_429", "sucesso"], registrador.Registros.Select(r => r.Status));
        Assert.Null(registrador.Registros[0].TokensEntrada);
    }

    [Fact]
    public async Task Consumo_nao_informado_fica_nulo_e_nunca_zero()
    {
        var (gerador, registrador, _) = Montar(Sucesso(1, tokens: null));

        await gerador.GerarAsync(["a"], null, "corr", Cancelamento);

        Assert.Null(Assert.Single(registrador.Registros).TokensEntrada);
    }

    [Fact]
    public async Task Dimensao_diferente_da_coluna_e_recusada()
    {
        var (gerador, _, _) = Montar(Sucesso(1, dimensao: 3072));

        await Assert.ThrowsAsync<InvalidOperationException>(() => gerador.GerarAsync(["a"], null, "corr", Cancelamento));
    }
}
