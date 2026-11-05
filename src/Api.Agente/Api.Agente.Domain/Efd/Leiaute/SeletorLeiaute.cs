using System.Globalization;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Domain.Efd.Leiaute;

public sealed record SelecaoLeiaute(DefinicaoLeiaute? Leiaute, string? MotivoRecusa);

/// <summary>
/// Valida o COD_VER contra o DT_FIN do registro 0000 (Guia Prático 3.2.4, registro 0000, campo 02;
/// NT 2026.001, item 3.1.1). Só são aceitas as versões disponíveis em dados (FR-003, FR-004).
/// </summary>
public static class SeletorLeiaute
{
    public static SelecaoLeiaute Selecionar(IdentificacaoArquivo identificacao, IReadOnlyList<DefinicaoLeiaute> leiautes)
    {
        var leiaute = leiautes.FirstOrDefault(l => l.CodVer == identificacao.CodVer);
        var periodoSuportado = leiautes.Any(l => Vigente(l, identificacao.PeriodoFim));

        if (leiaute is not null && Vigente(leiaute, identificacao.PeriodoFim))
        {
            return new SelecaoLeiaute(leiaute, null);
        }

        var aceitas = string.Join("; ", leiautes.Select(Descrever));
        var motivo = !periodoSuportado
            ? $"Período não suportado: DT_FIN {Formatar(identificacao.PeriodoFim)}. Aceitos: {aceitas}."
            : $"Versão de leiaute {identificacao.CodVer} não é válida para DT_FIN {Formatar(identificacao.PeriodoFim)}. Aceitos: {aceitas}.";
        return new SelecaoLeiaute(null, motivo);
    }

    private static bool Vigente(DefinicaoLeiaute leiaute, DateOnly data) =>
        data >= leiaute.Vigencia.Inicio && data <= leiaute.Vigencia.Fim;

    private static string Descrever(DefinicaoLeiaute leiaute) =>
        $"leiaute {leiaute.CodVer} para DT_FIN de {Formatar(leiaute.Vigencia.Inicio)} a {Formatar(leiaute.Vigencia.Fim)}";

    private static string Formatar(DateOnly data) => data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
