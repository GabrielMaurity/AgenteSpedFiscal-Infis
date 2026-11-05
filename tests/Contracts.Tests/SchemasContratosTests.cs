using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;

namespace Contracts.Tests;

/// <summary>Valida exemplos válidos e inválidos contra os contratos da spec 001 (FR-030, P-03, P-11).</summary>
public class SchemasContratosTests
{
    private const string AvisoPva =
        "Sem achados nas regras implementadas não equivale a conformidade. A validação oficial do PVA permanece obrigatória.";

    private static readonly ConcurrentDictionary<string, JsonSchema> Schemas = new();

    private static string CaminhoContrato(string arquivo) => Path.Combine(AppContext.BaseDirectory, "Contratos", arquivo);

    private static JsonSchema Schema(string arquivo) =>
        Schemas.GetOrAdd(arquivo, a => JsonSchema.FromText(File.ReadAllText(CaminhoContrato(a))));

    private static bool Valido(string arquivo, string json)
    {
        using var documento = JsonDocument.Parse(json);
        return Schema(arquivo).Evaluate(documento.RootElement).IsValid;
    }

    private static string RegraAtiva(string campos = """
        "fonte": { "documento": "Guia Prático da EFD-ICMS/IPI", "versao": "3.2.4", "localizacao": "Cap. II, Seção 1" },
        """) => $$"""
        {
          "versaoCatalogo": "1.0.0",
          "fontesOficiais": [{ "documento": "Guia Prático da EFD-ICMS/IPI", "versao": "3.2.4" }],
          "regras": [{
            "id": "EFD-ESTR-001",
            "descricao": "Abertura e encerramento de bloco",
            "tipo": "estrutural",
            "severidade": "impeditiva",
            "status": "ativa",
            "aplicacao": { "codVer": ["020"], "periodoInicio": "2026-01" },
            {{campos}}
            "acaoCorretiva": "Corrigir o gerador do arquivo."
          }]
        }
        """;

    private static string Envelope(string achado = "") => $$"""
        {
          "versaoContrato": "1.0.0",
          "execucaoId": "019a5c2e-7b1f-7c3a-9d4e-1f2a3b4c5d6e",
          "correlacaoId": "abc",
          "status": "concluida_com_limitacoes",
          "criadoEm": "2026-11-05T12:00:00Z",
          "etapas": [{ "numero": 1, "nome": "Receber arquivo", "status": "concluida" }],
          "regrasExecutadas": ["EFD-ESTR-001"],
          "validacoesNaoRealizadas": [{ "referencia": "etapa-5", "categoria": "falha_tecnica", "motivo": "base indisponível" }],
          "achados": [{{achado}}],
          "totaisPorRegra": { "EFD-ESTR-001": 1 },
          "revisaoHumanaNecessaria": true,
          "avisoPva": "{{AvisoPva}}"
        }
        """;

    private const string AchadoEstrutural = """
        {
          "id": "a1", "categoria": "erro_estrutural", "severidade": "impeditiva", "origem": "validador",
          "regraId": "EFD-ESTR-001", "linha": 10, "registro": "K990", "mensagem": "Falta o encerramento.",
          "fonte": { "documento": "Guia", "versao": "3.2.4", "localizacao": "Cap. II" }
        }
        """;

    private static string RespostaLlm(string severidadeIndicio = "advertencia", string extra = "") => $$"""
        {
          "versaoContrato": "1.0.0",
          "resumo": "Resumo.",
          "explicacoes": [{ "achadoId": "a1", "texto": "t", "causaProvavel": "c", "acaoCorretiva": "a", "trechoIds": ["t1"] }],
          "indicios": [{ "descricao": "d", "severidade": "{{severidadeIndicio}}", "registro": null, "trechoIds": [] }],
          "limitacoes": [],
          "revisaoHumanaRecomendada": true{{extra}}
        }
        """;

    [Fact]
    public void Catalogo_versionado_segue_o_contrato()
    {
        var catalogo = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Dados", "catalogo-regras-efd-icms-ipi.json"));

        Assert.True(Valido("catalogo-regras.schema.json", catalogo));
    }

    [Fact]
    public void Catalogo_com_regra_ativa_e_fonte_e_valido() =>
        Assert.True(Valido("catalogo-regras.schema.json", RegraAtiva()));

    [Fact]
    public void Catalogo_com_regra_ativa_sem_fonte_e_invalido() =>
        Assert.False(Valido("catalogo-regras.schema.json", RegraAtiva(campos: "")));

    [Fact]
    public void Catalogo_com_fonte_incompleta_e_invalido() =>
        Assert.False(Valido("catalogo-regras.schema.json", RegraAtiva("""
            "fonte": { "documento": "Guia", "versao": "3.2.4" },
            """)));

    [Fact]
    public void Catalogo_com_id_fora_do_padrao_e_invalido() =>
        Assert.False(Valido("catalogo-regras.schema.json", RegraAtiva().Replace("EFD-ESTR-001", "REGRA-1", StringComparison.Ordinal)));

    [Fact]
    public void Catalogo_sem_fontes_oficiais_e_invalido() =>
        Assert.False(Valido("catalogo-regras.schema.json", """{ "versaoCatalogo": "1.0.0", "fontesOficiais": [], "regras": [] }"""));

    [Fact]
    public void Envelope_com_achado_deterministico_e_valido() =>
        Assert.True(Valido("envelope-validacao.schema.json", Envelope(AchadoEstrutural)));

    [Fact]
    public void Envelope_com_status_desconhecido_e_invalido() =>
        Assert.False(Valido("envelope-validacao.schema.json",
            Envelope().Replace("concluida_com_limitacoes", "conforme", StringComparison.Ordinal)));

    [Fact]
    public void Envelope_sem_aviso_do_pva_e_invalido() =>
        Assert.False(Valido("envelope-validacao.schema.json",
            Envelope().Replace(AvisoPva, "Arquivo conforme.", StringComparison.Ordinal)));

    [Fact]
    public void Erro_estrutural_com_origem_ia_e_invalido() =>
        Assert.False(Valido("envelope-validacao.schema.json",
            Envelope(AchadoEstrutural.Replace("\"validador\"", "\"ia\"", StringComparison.Ordinal))));

    [Fact]
    public void Erro_estrutural_sem_regra_e_invalido() =>
        Assert.False(Valido("envelope-validacao.schema.json",
            Envelope(AchadoEstrutural.Replace("\"regraId\": \"EFD-ESTR-001\", ", "", StringComparison.Ordinal))));

    [Fact]
    public void Indicio_impeditivo_e_invalido() =>
        Assert.False(Valido("envelope-validacao.schema.json", Envelope("""
            { "id": "a2", "categoria": "indicio_ia", "severidade": "impeditiva", "origem": "ia", "mensagem": "m" }
            """)));

    [Fact]
    public void Resposta_do_llm_valida() =>
        Assert.True(Valido("llm-analise.schema.json", RespostaLlm()));

    [Fact]
    public void Resposta_do_llm_com_indicio_impeditivo_e_invalida() =>
        Assert.False(Valido("llm-analise.schema.json", RespostaLlm("impeditiva")));

    [Theory]
    [InlineData("execucaoId")]
    [InlineData("status")]
    [InlineData("tokensEntrada")]
    public void Resposta_do_llm_nao_aceita_metadados_do_sistema(string campo) =>
        Assert.False(Valido("llm-analise.schema.json", RespostaLlm(extra: $$""", "{{campo}}": "x" """)));

    [Fact]
    public void Agent_card_anuncia_as_duas_skills_e_exige_autenticacao()
    {
        using var cartao = JsonDocument.Parse(File.ReadAllText(CaminhoContrato("a2a-agent-card.json")));
        var raiz = cartao.RootElement;

        var skills = raiz.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("id").GetString()).ToArray();

        Assert.Equal(["validar-efd-icms-ipi", "consultar-efd-icms-ipi"], skills);
        Assert.Equal(1, raiz.GetProperty("securityRequirements").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(raiz.GetProperty("version").GetString()));
    }
}
