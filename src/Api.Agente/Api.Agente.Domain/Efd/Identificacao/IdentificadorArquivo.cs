using System.Globalization;
using Api.Agente.Domain.Efd.Leitura;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Domain.Efd.Identificacao;

public sealed record ResultadoIdentificacao(IdentificacaoArquivo? Identificacao, string? MotivoRecusa);

/// <summary>Lê a identificação do registro 0000 (Guia Prático 3.2.4, Cap. III, registro 0000).</summary>
public static class IdentificadorArquivo
{
    private const int CampoCodVer = 1;
    private const int CampoCodFin = 2;
    private const int CampoDtIni = 3;
    private const int CampoDtFin = 4;
    private const int CampoCnpj = 6;
    private const int CampoUf = 8;
    private const int CampoIndPerfil = 13;

    public static ResultadoIdentificacao Identificar(LinhaEfd? primeira)
    {
        if (primeira is null || !primeira.TemDelimitadores)
        {
            return Recusar("A primeira linha do arquivo não é um registro válido.");
        }

        var campos = primeira.Campos();
        if (campos[0] != "0000")
        {
            return Recusar("A primeira linha do arquivo não é o registro 0000.");
        }

        if (campos.Length <= CampoIndPerfil)
        {
            return Recusar("O registro 0000 não tem todos os campos de identificação.");
        }

        if (!TentarData(campos[CampoDtIni], out var inicio) || !TentarData(campos[CampoDtFin], out var fim))
        {
            return Recusar("DT_INI ou DT_FIN do registro 0000 não é uma data válida no formato ddmmaaaa.");
        }

        return new ResultadoIdentificacao(
            new IdentificacaoArquivo(
                campos[CampoCodVer], inicio, fim, campos[CampoCodFin], campos[CampoUf], campos[CampoCnpj], campos[CampoIndPerfil]),
            null);
    }

    private static ResultadoIdentificacao Recusar(string motivo) => new(null, motivo);

    private static bool TentarData(string valor, out DateOnly data) =>
        DateOnly.TryParseExact(valor, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out data);
}
