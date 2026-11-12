using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Agente.Application.Analise;
using Api.Agente.Infrastructure.Llm.Ferramentas;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Api.Agente.Infrastructure.Llm;

/// <summary>
/// Autorização de ferramentas antes de cada chamada (P-15, research R-11): a escolha do modelo não autoriza
/// nada. Verifica a lista permitida para o tipo de sessão, os papéis, os argumentos e o limite de chamadas;
/// aplica timeout e registra toda chamada, inclusive as negadas, que voltam ao modelo como erro.
/// </summary>
public sealed partial class FiltroAutorizacaoFerramentas(
    SessaoFerramentas sessao,
    int limiteChamadas,
    TimeSpan timeout,
    ILogger logger) : IFunctionInvocationFilter
{
    public const string PrefixoNegacao = "Erro: chamada negada pelo sistema.";

    private static readonly HashSet<string> PapeisAutorizados = new(StringComparer.Ordinal)
    {
        "analista",
        "agente_externo",
        SessaoFerramentas.PapelSistema,
    };

    private static readonly HashSet<string> Leitura = new(StringComparer.Ordinal)
    {
        FerramentasAgente.ConsultarManual,
        FerramentasAgente.ObterTrecho,
        FerramentasAgente.ConsultarRegra,
        FerramentasAgente.ObterAchados,
    };

    /// <summary>Ferramentas permitidas para a sessão. Em conversa, obter_achados exige execução vinculada.</summary>
    public static bool Permitida(SessaoFerramentas sessao, string ferramenta) =>
        Leitura.Contains(ferramenta)
        && (ferramenta != FerramentasAgente.ObterAchados || sessao.Tipo == TipoSessao.Validacao || sessao.TemExecucao);

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var ferramenta = context.Function.Name;
        var negacao = Autorizar(ferramenta, context.Arguments);
        if (!sessao.ReservarChamada(limiteChamadas))
        {
            negacao ??= $"Limite de {limiteChamadas} chamadas de ferramenta por execução atingido.";
        }

        if (negacao is not null)
        {
            Negar(context, ferramenta, negacao);
            return;
        }

        var cronometro = Stopwatch.StartNew();
        try
        {
            await next(context).WaitAsync(timeout, context.CancellationToken);
            Registrar(ferramenta, true, null, cronometro.ElapsedMilliseconds);
        }
        catch (TimeoutException)
        {
            context.Result = new FunctionResult(context.Function, $"Erro: a ferramenta excedeu o tempo de {timeout.TotalSeconds:0} s.");
            Registrar(ferramenta, true, "timeout", cronometro.ElapsedMilliseconds);
        }
        catch (Exception excecao) when (!context.CancellationToken.IsCancellationRequested)
        {
            // A mensagem interna não vai ao modelo: pode conter detalhes de infraestrutura.
            var motivo = excecao is BaseIndisponivelException ? "base de informação indisponível" : "falha ao executar a ferramenta";
            context.Result = new FunctionResult(context.Function, $"Erro: {motivo}.");
            Registrar(ferramenta, true, motivo, cronometro.ElapsedMilliseconds);
        }
    }

    private string? Autorizar(string ferramenta, KernelArguments argumentos)
    {
        if (!Permitida(sessao, ferramenta))
        {
            return $"A ferramenta {ferramenta} não está autorizada para esta {(sessao.Tipo == TipoSessao.Validacao ? "validação" : "conversa")}.";
        }

        if (!sessao.Papeis.Any(PapeisAutorizados.Contains))
        {
            return "O usuário não tem papel autorizado a usar ferramentas.";
        }

        return ValidarArgumentos(ferramenta, argumentos);
    }

    private static string? ValidarArgumentos(string ferramenta, KernelArguments argumentos)
    {
        switch (ferramenta)
        {
            case FerramentasAgente.ConsultarManual:
                var consulta = Valor(argumentos, "consulta");
                if (string.IsNullOrWhiteSpace(consulta) || consulta.Length > 1_000)
                {
                    return "consulta deve ter de 1 a 1.000 caracteres.";
                }

                return Valor(argumentos, "periodo") is { } periodo && !PadraoPeriodo().IsMatch(periodo)
                    ? "periodo deve estar no formato AAAA-MM."
                    : null;

            case FerramentasAgente.ObterTrecho:
                return Guid.TryParse(Valor(argumentos, "trechoId"), out _) ? null : "trechoId deve ser um identificador válido.";

            case FerramentasAgente.ConsultarRegra:
                return Valor(argumentos, "regraId") is { } regra && PadraoRegra().IsMatch(regra) ? null : "regraId inválido.";

            case FerramentasAgente.ObterAchados:
                if (Valor(argumentos, "regraId") is { } filtro && !PadraoRegra().IsMatch(filtro))
                {
                    return "regraId inválido.";
                }

                return Valor(argumentos, "pagina") is { } pagina
                    && (!int.TryParse(pagina, NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero is < 1 or > 10_000)
                    ? "pagina deve ser um número de 1 a 10.000."
                    : null;

            default:
                return "Ferramenta desconhecida.";
        }
    }

    /// <summary>Argumentos chegam como texto ou JSON, conforme o conector; nulo quando ausentes.</summary>
    private static string? Valor(KernelArguments argumentos, string nome)
    {
        if (!argumentos.TryGetValue(nome, out var valor) || valor is null)
        {
            return null;
        }

        return valor switch
        {
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement { ValueKind: JsonValueKind.String } elemento => elemento.GetString(),
            JsonElement elemento => elemento.GetRawText(),
            _ => Convert.ToString(valor, CultureInfo.InvariantCulture),
        };
    }

    private void Negar(FunctionInvocationContext context, string ferramenta, string motivo)
    {
        context.Result = new FunctionResult(context.Function, $"{PrefixoNegacao} {motivo}");
        Registrar(ferramenta, false, motivo, 0);
    }

    private void Registrar(string ferramenta, bool autorizada, string? motivo, long duracaoMs)
    {
        sessao.Registrar(new ChamadaFerramenta(ferramenta, autorizada, motivo, duracaoMs));
        Log.Chamada(logger, ferramenta, autorizada, motivo, duracaoMs, sessao.CorrelacaoId);
    }

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex PadraoPeriodo();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,39}$")]
    private static partial Regex PadraoRegra();

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Ferramenta {Ferramenta}: autorizada={Autorizada}, motivo={Motivo}, duração {DuracaoMs} ms, correlação {CorrelacaoId}.")]
        public static partial void Chamada(ILogger logger, string ferramenta, bool autorizada, string? motivo, long duracaoMs, string correlacaoId);
    }
}
