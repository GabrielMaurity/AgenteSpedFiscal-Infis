using System.Text.Json;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Analise;

namespace Api.Agente.Application.Analise;

public sealed record ResultadoValidacaoLlm<T>(T? Conteudo, IReadOnlyList<string> Erros)
    where T : class
{
    public bool Valida => Conteudo is not null && Erros.Count == 0;
}

/// <summary>Resposta do LLM em conversa, no formato de llm-conversa.schema.json.</summary>
public sealed record RespostaConversaLlm(string Resposta, bool Fundamentada, bool DependeDoArquivo, List<string> TrechoIds, List<string> Limitacoes);

/// <summary>
/// Validação estrutural e semântica da saída do LLM (research R-10, P-13). A estrutura segue os contratos
/// llm-analise e llm-conversa: campos desconhecidos são recusados, o que impede o modelo de tentar alterar
/// achados, status ou metadados. A semântica exige achados e trechos existentes nesta execução.
/// </summary>
public static class ValidadorRespostaLlm
{
    public const string VersaoContrato = "1.0.0";

    private static readonly string[] CamposAnalise = ["versaoContrato", "resumo", "explicacoes", "indicios", "limitacoes", "revisaoHumanaRecomendada"];
    private static readonly string[] CamposExplicacao = ["achadoId", "texto", "causaProvavel", "acaoCorretiva", "trechoIds"];
    private static readonly string[] CamposIndicio = ["descricao", "severidade", "registro", "trechoIds"];
    private static readonly string[] CamposConversa = ["versaoContrato", "resposta", "fundamentada", "dependeDoArquivo", "trechoIds", "limitacoes"];

    public static ResultadoValidacaoLlm<ConteudoAnaliseIa> ValidarAnalise(
        string resposta, IReadOnlySet<string> achadosDeterministicos, ContextoRecuperacao recuperacao)
    {
        var erros = new List<string>();
        using var documento = Ler(resposta, erros);
        if (documento is null || !Objeto(documento.RootElement, "resposta", CamposAnalise, erros))
        {
            return new(null, erros);
        }

        var raiz = documento.RootElement;
        VerificarVersao(raiz, erros);
        var resumo = Texto(raiz, "resumo", erros);
        var revisao = Booleano(raiz, "revisaoHumanaRecomendada", erros);
        var limitacoes = ListaTextos(raiz, "limitacoes", erros);

        var explicacoes = new List<ExplicacaoAchado>();
        foreach (var (item, indice) in Itens(raiz, "explicacoes", erros))
        {
            var caminho = $"explicacoes[{indice}]";
            if (!Objeto(item, caminho, CamposExplicacao, erros))
            {
                continue;
            }

            var achadoId = Texto(item, "achadoId", erros, caminho);
            if (achadoId is not null && !achadosDeterministicos.Contains(achadoId))
            {
                erros.Add($"{caminho}.achadoId \"{achadoId}\" não existe entre os achados desta execução.");
            }

            var trechos = TrechosCitados(item, caminho, recuperacao, erros);
            explicacoes.Add(new ExplicacaoAchado(
                achadoId ?? "",
                Texto(item, "texto", erros, caminho) ?? "",
                Texto(item, "causaProvavel", erros, caminho) ?? "",
                Texto(item, "acaoCorretiva", erros, caminho) ?? "",
                trechos));
        }

        var indicios = new List<IndicioAnalise>();
        foreach (var (item, indice) in Itens(raiz, "indicios", erros))
        {
            var caminho = $"indicios[{indice}]";
            if (!Objeto(item, caminho, CamposIndicio, erros))
            {
                continue;
            }

            var severidade = Texto(item, "severidade", erros, caminho) switch
            {
                "advertencia" => Severidade.Advertencia,
                "informativa" => Severidade.Informativa,
                "impeditiva" => Erro<Severidade>(erros, $"{caminho}.severidade: indício da IA nunca é impeditivo."),
                var outra => Erro<Severidade>(erros, $"{caminho}.severidade \"{outra}\" fora da lista (advertencia, informativa)."),
            };

            string? registro = null;
            if (item.TryGetProperty("registro", out var valorRegistro) && valorRegistro.ValueKind != JsonValueKind.Null)
            {
                registro = Texto(item, "registro", erros, caminho);
            }

            var trechos = TrechosCitados(item, caminho, recuperacao, erros);
            if (trechos.Count == 0)
            {
                erros.Add($"{caminho}: indício precisa citar ao menos um trecho recuperado.");
            }

            indicios.Add(new IndicioAnalise(Texto(item, "descricao", erros, caminho) ?? "", severidade, registro, trechos));
        }

        return erros.Count > 0
            ? new(null, erros)
            : new(new ConteudoAnaliseIa(VersaoContrato, resumo!, explicacoes, indicios, limitacoes, revisao!.Value), erros);
    }

    public static ResultadoValidacaoLlm<RespostaConversaLlm> ValidarConversa(string resposta, ContextoRecuperacao recuperacao)
    {
        var erros = new List<string>();
        using var documento = Ler(resposta, erros);
        if (documento is null || !Objeto(documento.RootElement, "resposta", CamposConversa, erros))
        {
            return new(null, erros);
        }

        var raiz = documento.RootElement;
        VerificarVersao(raiz, erros);
        var texto = Texto(raiz, "resposta", erros);
        var fundamentada = Booleano(raiz, "fundamentada", erros);
        var dependeDoArquivo = Booleano(raiz, "dependeDoArquivo", erros);
        var limitacoes = ListaTextos(raiz, "limitacoes", erros);
        var trechos = TrechosCitados(raiz, "resposta", recuperacao, erros);

        if (fundamentada == true && trechos.Count == 0)
        {
            erros.Add("resposta fundamentada precisa citar ao menos um trecho recuperado.");
        }

        return erros.Count > 0
            ? new(null, erros)
            : new(new RespostaConversaLlm(texto!, fundamentada!.Value, dependeDoArquivo!.Value, trechos, limitacoes), erros);
    }

    /// <summary>Referências com os dados da base, nunca do modelo, e o cálculo de fundamentação (R-27).</summary>
    public static AnaliseIa MontarAnaliseAceita(ConteudoAnaliseIa conteudo, ContextoRecuperacao recuperacao, string modelo, int tentativas)
    {
        var citados = conteudo.Explicacoes.SelectMany(e => e.TrechoIds).Concat(conteudo.Indicios.SelectMany(i => i.TrechoIds));
        var referencias = Referencias(citados, recuperacao);
        var final = conteudo with { Limitacoes = Fundamentacao.Limitacoes(conteudo.Limitacoes, referencias) };
        return new AnaliseIa(StatusAnaliseIa.Aceita, modelo, tentativas, final, referencias, Fundamentacao.EmFonteOficial(referencias));
    }

    public static List<ReferenciaTrecho> Referencias(IEnumerable<string> trechoIds, ContextoRecuperacao recuperacao) =>
        trechoIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(recuperacao.Obter)
            .OfType<TrechoBase>()
            .Select(t => t.ComoReferencia())
            .ToList();

    private static JsonDocument? Ler(string resposta, List<string> erros)
    {
        try
        {
            return JsonDocument.Parse(resposta);
        }
        catch (JsonException)
        {
            erros.Add("a resposta não é um JSON válido.");
            return null;
        }
    }

    private static bool Objeto(JsonElement elemento, string caminho, string[] campos, List<string> erros)
    {
        if (elemento.ValueKind != JsonValueKind.Object)
        {
            erros.Add($"{caminho} deve ser um objeto.");
            return false;
        }

        var antes = erros.Count;
        foreach (var propriedade in elemento.EnumerateObject().Where(p => !campos.Contains(p.Name)))
        {
            erros.Add($"{caminho}: campo \"{propriedade.Name}\" fora do contrato; a análise não altera achados, status nem metadados.");
        }

        foreach (var campo in campos.Where(c => !elemento.TryGetProperty(c, out _)))
        {
            erros.Add($"{caminho}: campo obrigatório \"{campo}\" ausente.");
        }

        return erros.Count == antes;
    }

    private static void VerificarVersao(JsonElement raiz, List<string> erros)
    {
        if (Texto(raiz, "versaoContrato", erros) is { } versao && versao != VersaoContrato)
        {
            erros.Add($"versaoContrato deve ser \"{VersaoContrato}\".");
        }
    }

    private static string? Texto(JsonElement elemento, string campo, List<string> erros, string? caminho = null)
    {
        if (elemento.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String)
        {
            return valor.GetString();
        }

        erros.Add($"{Nome(caminho, campo)} deve ser texto.");
        return null;
    }

    private static bool? Booleano(JsonElement elemento, string campo, List<string> erros)
    {
        if (elemento.TryGetProperty(campo, out var valor) && valor.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return valor.GetBoolean();
        }

        erros.Add($"{campo} deve ser verdadeiro ou falso.");
        return null;
    }

    private static List<string> ListaTextos(JsonElement elemento, string campo, List<string> erros, string? caminho = null)
    {
        var lista = new List<string>();
        foreach (var (item, indice) in Itens(elemento, campo, erros, caminho))
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                lista.Add(item.GetString()!);
            }
            else
            {
                erros.Add($"{Nome(caminho, campo)}[{indice}] deve ser texto.");
            }
        }

        return lista;
    }

    private static List<(JsonElement Item, int Indice)> Itens(JsonElement elemento, string campo, List<string> erros, string? caminho = null)
    {
        if (!elemento.TryGetProperty(campo, out var valor) || valor.ValueKind != JsonValueKind.Array)
        {
            erros.Add($"{Nome(caminho, campo)} deve ser uma lista.");
            return [];
        }

        return [.. valor.EnumerateArray().Select((item, indice) => (item, indice))];
    }

    private static List<string> TrechosCitados(JsonElement elemento, string caminho, ContextoRecuperacao recuperacao, List<string> erros)
    {
        var trechos = ListaTextos(elemento, "trechoIds", erros, caminho);
        foreach (var trecho in trechos.Where(t => !recuperacao.Contem(t)))
        {
            erros.Add($"{caminho}.trechoIds: o trecho \"{trecho}\" não foi recuperado nesta execução.");
        }

        return trechos;
    }

    private static T Erro<T>(List<string> erros, string mensagem)
        where T : struct
    {
        erros.Add(mensagem);
        return default;
    }

    private static string Nome(string? caminho, string campo) => caminho is null ? campo : $"{caminho}.{campo}";
}
