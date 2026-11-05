using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Estrutural;

/// <summary>Início e fim do arquivo, ordem dos blocos, abertura e encerramento (FR-011).</summary>
public sealed class ValidadorBlocos : IValidadorEstrutural
{
    private const string RegistroInicial = "0000";
    private const string RegistroFinal = "9999";

    private readonly HashSet<string> _vistos = [];
    private readonly HashSet<string> _blocosEncerrados = [];
    private int _ordemAtual = -1;
    private long _totalLinhas;
    private string? _ultimoRegistro;
    private long _linhaUltimoRegistro;

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        var codigo = campos[0];
        _totalLinhas++;
        _ultimoRegistro = codigo;
        _linhaUltimoRegistro = linha.Numero;
        _vistos.Add(codigo);

        if (_totalLinhas == 1 && codigo != RegistroInicial)
        {
            contexto.Emitir(RegrasEstruturais.InicioEFim, "O arquivo não começa no registro 0000.", linha.Numero, codigo, esperado: RegistroInicial);
        }

        if (codigo is RegistroInicial or RegistroFinal || definicao is null)
        {
            return;
        }

        var bloco = contexto.Leiaute.Blocos.FirstOrDefault(b => b.Codigo == definicao.Bloco);
        if (bloco is null)
        {
            return;
        }

        if (bloco.Ordem < _ordemAtual || _blocosEncerrados.Contains(bloco.Codigo))
        {
            contexto.Emitir(
                RegrasEstruturais.OrdemBlocos,
                $"O registro {codigo} do bloco {bloco.Codigo} está fora da sequência dos blocos.",
                linha.Numero,
                codigo);
        }

        _ordemAtual = Math.Max(_ordemAtual, bloco.Ordem);
        if (codigo == bloco.Encerramento)
        {
            _blocosEncerrados.Add(bloco.Codigo);
        }
    }

    public void Finalizar(ContextoValidacao contexto)
    {
        if (_totalLinhas == 0)
        {
            contexto.Emitir(RegrasEstruturais.InicioEFim, "O arquivo está vazio.", esperado: RegistroInicial);
        }
        else if (_ultimoRegistro != RegistroFinal)
        {
            contexto.Emitir(
                RegrasEstruturais.InicioEFim,
                "O arquivo não termina no registro 9999.",
                _linhaUltimoRegistro,
                _ultimoRegistro,
                esperado: RegistroFinal);
        }

        foreach (var bloco in contexto.Leiaute.Blocos)
        {
            ExigirPresenca(bloco, bloco.Abertura, "abertura", contexto);
            ExigirPresenca(bloco, bloco.Encerramento, "encerramento", contexto);
        }
    }

    private void ExigirPresenca(DefinicaoBloco bloco, string registro, string papel, ContextoValidacao contexto)
    {
        if (!_vistos.Contains(registro))
        {
            contexto.Emitir(
                RegrasEstruturais.AberturaEncerramento,
                $"Falta o registro {registro}, de {papel} do bloco {bloco.Codigo}.",
                registro: registro);
        }
    }
}
