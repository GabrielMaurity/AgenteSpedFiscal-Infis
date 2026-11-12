using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Api.Agente.Application.Conversas;
using Api.Agente.Domain.Analise;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Api.Agente.Integration.Tests.Conversas;

/// <summary>Conversas com MongoDB real, base e modelo falsos (FR-050 a FR-054, research R-27 e R-29).</summary>
[Collection(ColecaoContainers.Nome)]
[Trait("Category", "Docker")]
public sealed class ConversasTests : IAsyncDisposable
{
    private readonly string _temporario = Path.Combine(Path.GetTempPath(), $"testes-conversas-{Guid.NewGuid():N}");
    private readonly ContainersFixture _containers;
    private readonly List<FabricaApiAgente> _fabricas = [];

    public ConversasTests(ContainersFixture containers) => _containers = containers;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        foreach (var fabrica in _fabricas)
        {
            await fabrica.DisposeAsync();
        }

        if (Directory.Exists(_temporario))
        {
            Directory.Delete(_temporario, recursive: true);
        }
    }

    private FabricaApiAgente Fabrica(BaseFalsa baseInformacao, ChatFalso chat)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao["MongoDb:ConnectionString"] = _containers.MongoConnectionString;
        configuracao["MongoDb:Database"] = $"testes_{Guid.NewGuid():N}";
        configuracao["Postgres:ConnectionString"] = _containers.PostgresConnectionString;
        configuracao["Arquivos:DiretorioTemporario"] = _temporario;
        var fabrica = new FabricaApiAgente { Configuracao = configuracao, Servicos = ServicosIa.Substituir(baseInformacao, chat) };
        _fabricas.Add(fabrica);
        return fabrica;
    }

    private static HttpClient Cliente(FabricaApiAgente fabrica, string usuario = "usuario-a", string organizacao = "org-a")
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", FabricaApiAgente.CriarTokenUsuario(usuario, organizacao, FabricaApiAgente.Audiencia, Papeis.Analista));
        return cliente;
    }

    private static async Task<Guid> CriarConversaAsync(HttpClient cliente, Guid? execucaoId = null)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/v1/conversas", new { execucaoId }, Cancelamento);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        return corpo.RootElement.GetProperty("conversaId").GetGuid();
    }

    private static async Task<JsonDocument> PerguntarAsync(HttpClient cliente, Guid conversaId, string texto, string? periodo = "2026-03")
    {
        var resposta = await cliente.PostAsJsonAsync($"/api/v1/conversas/{conversaId}/mensagens", new { texto, periodo }, Cancelamento);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
    }

    [Fact]
    public async Task Usuario_nao_le_conversa_de_outro_usuario_nem_de_outra_organizacao()
    {
        var fabrica = Fabrica(new BaseFalsa(), ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("ok", true, false, BaseFalsa.Oficial.TrechoId)));
        var dono = Cliente(fabrica);
        var conversaId = await CriarConversaAsync(dono);
        (await PerguntarAsync(dono, conversaId, "pergunta")).Dispose();

        foreach (var outro in new[] { Cliente(fabrica, "usuario-b", "org-a"), Cliente(fabrica, "usuario-a", "org-b") })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync(new Uri($"/api/v1/conversas/{conversaId}/mensagens", UriKind.Relative), Cancelamento)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outro.PostAsJsonAsync($"/api/v1/conversas/{conversaId}/mensagens", new { texto = "x" }, Cancelamento)).StatusCode);
        }

        var historico = await dono.GetFromJsonAsync<JsonElement>($"/api/v1/conversas/{conversaId}/mensagens", Cancelamento);
        Assert.Equal(2, historico.GetArrayLength());
    }

    [Fact]
    public async Task Resposta_com_fonte_oficial_cita_documento_versao_localizacao_e_classificacao()
    {
        var fabrica = Fabrica(new BaseFalsa(), ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("Não integra em 2026.", true, false, BaseFalsa.Oficial.TrechoId)));
        var cliente = Cliente(fabrica);

        using var resposta = await PerguntarAsync(cliente, await CriarConversaAsync(cliente), "O valor de IBS entra no VL_DOC do C100 em 2026?");

        var raiz = resposta.RootElement;
        Assert.True(raiz.GetProperty("fundamentada").GetBoolean());
        Assert.True(raiz.GetProperty("fundamentadaEmFonteOficial").GetBoolean());
        var referencia = Assert.Single(raiz.GetProperty("referencias").EnumerateArray());
        Assert.Equal(BaseFalsa.Oficial.TrechoId, referencia.GetProperty("trechoId").GetString());
        Assert.Equal("3.2.4", referencia.GetProperty("versao").GetString());
        Assert.Equal("Cap. I, Seção 10", referencia.GetProperty("localizacao").GetString());
        Assert.Equal("oficial", referencia.GetProperty("classificacao").GetString());
        Assert.Equal(0.82, raiz.GetProperty("proximidade").GetDouble(), precision: 4);
    }

    [Fact]
    public async Task Sem_citacao_a_proximidade_e_a_do_melhor_trecho_recuperado()
    {
        var fabrica = Fabrica(new BaseFalsa(), ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("Não encontrei.", false, false)));
        var cliente = Cliente(fabrica);

        using var resposta = await PerguntarAsync(cliente, await CriarConversaAsync(cliente), "Pergunta sem resposta na base");

        var raiz = resposta.RootElement;
        Assert.False(raiz.GetProperty("fundamentada").GetBoolean());
        Assert.Equal(0.82, raiz.GetProperty("proximidade").GetDouble(), precision: 4);
        Assert.Contains(ConversasHandler.SemEvidencia, raiz.GetProperty("limitacoes").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task Sem_trechos_na_base_a_proximidade_e_zero()
    {
        var vazia = new BaseFalsa();
        vazia.Trechos.Clear();
        var fabrica = Fabrica(vazia, ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("Não encontrei.", false, false)));
        var cliente = Cliente(fabrica);

        using var resposta = await PerguntarAsync(cliente, await CriarConversaAsync(cliente), "Pergunta qualquer");

        Assert.Equal(0.0, resposta.RootElement.GetProperty("proximidade").GetDouble());
    }

    [Fact]
    public async Task Conversa_sem_execucao_nao_fala_em_arquivo_descartado()
    {
        var fabrica = Fabrica(new BaseFalsa(), ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("Não sei.", false, true)));
        var cliente = Cliente(fabrica);

        using var resposta = await PerguntarAsync(cliente, await CriarConversaAsync(cliente), "O IBS entra no VL_DOC do C100 em 2026?");

        var limitacoes = resposta.RootElement.GetProperty("limitacoes").EnumerateArray().Select(l => l.GetString()).ToList();
        Assert.DoesNotContain(ConversasHandler.ArquivoDescartado, limitacoes);
        Assert.Contains(ConversasHandler.SemEvidencia, limitacoes);
    }

    [Fact]
    public async Task Resposta_so_com_trecho_interno_nao_e_fundamentada_em_fonte_oficial()
    {
        var baseInterna = new BaseFalsa();
        baseInterna.Trechos.Remove(BaseFalsa.Oficial);
        var fabrica = Fabrica(baseInterna, ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("Confira o C190.", true, false, BaseFalsa.Interno.TrechoId)));
        var cliente = Cliente(fabrica);

        using var resposta = await PerguntarAsync(cliente, await CriarConversaAsync(cliente), "Devo conferir o C190?");

        var raiz = resposta.RootElement;
        Assert.False(raiz.GetProperty("fundamentadaEmFonteOficial").GetBoolean());
        Assert.Equal("interno", Assert.Single(raiz.GetProperty("referencias").EnumerateArray()).GetProperty("classificacao").GetString());
        Assert.Contains(Fundamentacao.SemFonteOficial, raiz.GetProperty("limitacoes").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task Citacao_de_trecho_nao_recuperado_nunca_chega_a_resposta()
    {
        var inventado = Guid.NewGuid().ToString();
        var fabrica = Fabrica(new BaseFalsa(), ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("Inventada.", true, false, inventado)));
        var cliente = Cliente(fabrica);

        using var resposta = await PerguntarAsync(cliente, await CriarConversaAsync(cliente), "Pergunta qualquer");

        var raiz = resposta.RootElement;
        Assert.False(raiz.GetProperty("fundamentada").GetBoolean());
        Assert.Empty(raiz.GetProperty("referencias").EnumerateArray());
        Assert.DoesNotContain(inventado, raiz.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(ConversasHandler.RespostaInvalida, raiz.GetProperty("limitacoes").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task Pergunta_sobre_dado_do_arquivo_descartado_orienta_o_reenvio()
    {
        var fabrica = Fabrica(new BaseFalsa(), ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("O endereço não está nos achados.", false, true)));
        var cliente = Cliente(fabrica);
        var execucaoId = await ValidarAsync(cliente, "valido-perfil-a");

        using var resposta = await PerguntarAsync(cliente, await CriarConversaAsync(cliente, execucaoId), "Qual é o endereço do participante do 0150?", periodo: null);

        var raiz = resposta.RootElement;
        Assert.False(raiz.GetProperty("fundamentada").GetBoolean());
        Assert.Contains(ConversasHandler.ArquivoDescartado, raiz.GetProperty("limitacoes").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task No_maximo_vinte_mensagens_sao_enviadas_ao_modelo()
    {
        var chat = ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("ok", true, false, BaseFalsa.Oficial.TrechoId));
        var fabrica = Fabrica(new BaseFalsa(), chat);
        var cliente = Cliente(fabrica);
        var conversaId = await CriarConversaAsync(cliente);

        for (var i = 0; i < 12; i++)
        {
            (await PerguntarAsync(cliente, conversaId, $"pergunta {i}")).Dispose();
        }

        var ultimo = chat.Historicos[^1];
        Assert.Equal(AuthorRole.System, ultimo[0].Role);
        Assert.Equal(20, ultimo.Count - 1);
        Assert.Contains("pergunta 11", ultimo[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conversa_vinculada_a_execucao_de_outra_organizacao_nao_e_criada()
    {
        var fabrica = Fabrica(new BaseFalsa(), ChatFalso.Conversa(_ => "{}"));
        var execucaoId = await ValidarAsync(Cliente(fabrica, organizacao: "org-a"), "valido-perfil-a");

        var resposta = await Cliente(fabrica, organizacao: "org-b").PostAsJsonAsync("/api/v1/conversas", new { execucaoId }, Cancelamento);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Skill_a2a_de_consulta_usa_o_mesmo_caso_de_uso_e_o_mesmo_formato()
    {
        var fabrica = Fabrica(new BaseFalsa(), ChatFalso.Conversa(_ => ChatFalso.RespostaConversa("Não integra em 2026.", true, false, BaseFalsa.Oficial.TrechoId)));
        var http = fabrica.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", FabricaApiAgente.CriarTokenUsuario("agente-1", "org-a", FabricaApiAgente.Audiencia, Papeis.AgenteExterno));
        var pedido = JsonSerializer.SerializeToElement(new { skill = "consultar-efd-icms-ipi", pergunta = "O IBS entra no VL_DOC em 2026?", periodo = "2026-03" });

        var resposta = await new A2AClient(new Uri(http.BaseAddress!, "/a2a"), http).SendMessageAsync(
            new SendMessageRequest
            {
                Message = new Message { Role = Role.User, MessageId = Guid.NewGuid().ToString("N"), Parts = [Part.FromData(pedido)] },
            },
            Cancelamento);

        Assert.Equal(TaskState.Completed, resposta.Task!.Status.State);
        var dados = resposta.Task.Artifacts!.Single().Parts.Single().Data!.Value;
        var conteudo = dados.GetProperty("resposta");
        Assert.True(conteudo.GetProperty("fundamentadaEmFonteOficial").GetBoolean());
        Assert.Equal("oficial", conteudo.GetProperty("referencias")[0].GetProperty("classificacao").GetString());

        // A conversa criada por A2A pertence ao agente que chamou, com o mesmo isolamento do REST.
        var conversaId = dados.GetProperty("conversaId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await Cliente(fabrica, "usuario-a", "org-a").GetAsync(new Uri($"/api/v1/conversas/{conversaId}/mensagens", UriKind.Relative), Cancelamento)).StatusCode);
    }

    private static async Task<Guid> ValidarAsync(HttpClient cliente, string caso)
    {
        using var corpo = new MultipartFormDataContent
        {
            { new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", $"{caso}.txt"), Cancelamento)), "arquivo", $"{caso}.txt" },
        };
        var envio = await cliente.PostAsync(new Uri("/api/v1/validacoes", UriKind.Relative), corpo, Cancelamento);
        Assert.Equal(HttpStatusCode.Accepted, envio.StatusCode);
        for (var tentativa = 0; tentativa < 100; tentativa++)
        {
            using var documento = JsonDocument.Parse(await cliente.GetStringAsync(envio.Headers.Location, Cancelamento));
            if (documento.RootElement.GetProperty("status").GetString() is "concluida" or "concluida_com_limitacoes" or "falha")
            {
                return documento.RootElement.GetProperty("execucaoId").GetGuid();
            }

            await Task.Delay(100, Cancelamento);
        }

        throw new TimeoutException("A execução não terminou no tempo esperado.");
    }
}
