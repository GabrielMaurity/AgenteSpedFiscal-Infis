using System.Text.Json;
using Api.Agente.Application.Analise;
using Api.Agente.Domain.Analise;

namespace Api.Agente.Application.Tests.Fakes;

/// <summary>Base de informação em memória. Indisponível por padrão, como a Api.Rag parada.</summary>
public sealed class BaseInformacaoFalsa : IBaseInformacao
{
    public static readonly TrechoBase TrechoOficial = new(
        "11111111-1111-1111-1111-111111111111", "Registro C190: totalização por CST, CFOP e alíquota.",
        ClassificacaoFonte.Oficial, "Guia Prático EFD ICMS/IPI", "3.2.4", "Cap. III, Seção 3, Registro C190, p. 120");

    public static readonly TrechoBase TrechoInterno = new(
        "22222222-2222-2222-2222-222222222222", "FAQ interno: confira o C190 antes de transmitir.",
        ClassificacaoFonte.Interno, "FAQ interno Infis", "1", "Pergunta 3");

    public bool Disponivel { get; set; }

    public List<TrechoBase> Trechos { get; } = [TrechoOficial, TrechoInterno];

    public List<ConsultaBase> Consultas { get; } = [];

    public static BaseInformacaoFalsa ComTrechos() => new() { Disponivel = true };

    public Task<ResultadoBusca> BuscarAsync(ConsultaBase consulta, ContextoRecuperacao contexto, CancellationToken cancelamento)
    {
        Consultas.Add(consulta);
        if (!Disponivel)
        {
            throw new BaseIndisponivelException("A base de informação não respondeu.");
        }

        contexto.Registrar(Trechos);
        return Task.FromResult(new ResultadoBusca([.. Trechos], Trechos.Count == 0));
    }

    public Task<TrechoBase?> ObterTrechoAsync(string trechoId, string correlacaoId, ContextoRecuperacao contexto, CancellationToken cancelamento)
    {
        if (!Disponivel)
        {
            throw new BaseIndisponivelException("A base de informação não respondeu.");
        }

        var trecho = Trechos.FirstOrDefault(t => t.TrechoId == trechoId);
        if (trecho is not null)
        {
            contexto.Registrar([trecho]);
        }

        return Task.FromResult(trecho);
    }
}

/// <summary>Modelo com respostas roteirizadas. Sem respostas, comporta-se como provedor indisponível.</summary>
public sealed class ModeloFalso(params string[] respostas) : IModeloAnalise
{
    private readonly Queue<string> _respostas = new(respostas);

    public string Modelo => "modelo-falso";

    public List<PedidoModelo> Pedidos { get; } = [];

    public Task<RespostaModelo> GerarAsync(PedidoModelo pedido, CancellationToken cancelamento)
    {
        Pedidos.Add(pedido);
        return _respostas.Count > 0
            ? Task.FromResult(new RespostaModelo(_respostas.Dequeue()))
            : throw new ModeloIndisponivelException("O provedor do modelo não respondeu.");
    }
}

/// <summary>Respostas no formato dos contratos llm-analise e llm-conversa.</summary>
public static class RespostasLlm
{
    public static string Analise(
        IEnumerable<object>? explicacoes = null,
        IEnumerable<object>? indicios = null,
        IEnumerable<string>? limitacoes = null,
        object? extra = null)
    {
        var resposta = new Dictionary<string, object?>
        {
            ["versaoContrato"] = "1.0.0",
            ["resumo"] = "Resumo da análise.",
            ["explicacoes"] = explicacoes?.ToList() ?? [],
            ["indicios"] = indicios?.ToList() ?? [],
            ["limitacoes"] = limitacoes?.ToList() ?? [],
            ["revisaoHumanaRecomendada"] = false,
        };
        if (extra is not null)
        {
            foreach (var propriedade in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            {
                resposta[propriedade.Name] = propriedade.Value;
            }
        }

        return JsonSerializer.Serialize(resposta);
    }

    public static object Explicacao(string achadoId, params string[] trechoIds) => new
    {
        achadoId,
        texto = "Explicação.",
        causaProvavel = "Causa.",
        acaoCorretiva = "Ação.",
        trechoIds,
    };

    public static object Indicio(string severidade, params string[] trechoIds) => new
    {
        descricao = "Indício encontrado na base.",
        severidade,
        registro = "C190",
        trechoIds,
    };

    public static string Conversa(string resposta, bool fundamentada, bool dependeDoArquivo = false, string[]? trechoIds = null, string[]? limitacoes = null) =>
        JsonSerializer.Serialize(new
        {
            versaoContrato = "1.0.0",
            resposta,
            fundamentada,
            dependeDoArquivo,
            trechoIds = trechoIds ?? [],
            limitacoes = limitacoes ?? [],
        });
}
