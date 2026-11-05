using System.Globalization;

namespace Api.Agente.Domain.Efd.Validacao.Negocio;

public static class RegrasNegocio
{
    public const string UnidadeCadastrada = "EFD-NEG-001";
    public const string FatorConversao = "EFD-NEG-002";
    public const string NcmVigente = "EFD-NEG-003";
    public const string TotaisAnaliticos = "EFD-NEG-004";
    public const string ValorDocumento = "EFD-NEG-005";
    public const string ValorMercadorias = "EFD-NEG-006";
    public const string ItensAnaliticos = "EFD-NEG-007";
    public const string LacunaNumeracao = "EFD-NEG-008";
    public const string TributosReforma = "EFD-NEG-009";

    public const string DependenciaTabelaNcm = "tabela:ncm";
    public const string DependenciaXmlDocumento = "xml:documento";
}

/// <summary>Valores numéricos da EFD: vírgula decimal, sem separador de milhar (NT 2026.001, item 2.2.1).</summary>
public static class ValorEfd
{
    /// <summary>Campo vazio vale zero. Valor inválido já é erro estrutural e impede a comparação.</summary>
    public static bool TentarLer(string texto, out decimal valor)
    {
        if (texto.Length == 0)
        {
            valor = 0m;
            return true;
        }

        return decimal.TryParse(texto.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor);
    }

    public static string Formatar(decimal valor) =>
        valor.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
}
