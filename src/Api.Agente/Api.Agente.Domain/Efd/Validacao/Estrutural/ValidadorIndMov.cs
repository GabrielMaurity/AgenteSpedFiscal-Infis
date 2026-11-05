using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Estrutural;

/// <summary>Bloco declarado sem dados não tem registros além da abertura e do encerramento (FR-013).</summary>
public sealed class ValidadorIndMov : IValidadorEstrutural
{
    private const string BlocoSemDados = "1";

    private readonly HashSet<string> _blocosSemDados = [];

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        if (definicao is null)
        {
            return;
        }

        var bloco = contexto.Leiaute.Blocos.FirstOrDefault(b => b.Codigo == definicao.Bloco);
        if (bloco is null || definicao.Codigo is "0000" or "9999" || definicao.Codigo == bloco.Encerramento)
        {
            return;
        }

        if (definicao.Codigo == bloco.Abertura)
        {
            // O segundo campo da abertura é o indicador (IND_MOV; IND_DAD no bloco B).
            if (campos.Length > 1 && campos[1] == BlocoSemDados)
            {
                _blocosSemDados.Add(bloco.Codigo);
            }

            return;
        }

        if (_blocosSemDados.Contains(bloco.Codigo))
        {
            contexto.Emitir(
                RegrasEstruturais.IndicadorMovimento,
                $"O bloco {bloco.Codigo} foi declarado sem dados, mas contém o registro {definicao.Codigo}.",
                linha.Numero,
                definicao.Codigo);
        }
    }

    public void Finalizar(ContextoValidacao contexto)
    {
    }
}
