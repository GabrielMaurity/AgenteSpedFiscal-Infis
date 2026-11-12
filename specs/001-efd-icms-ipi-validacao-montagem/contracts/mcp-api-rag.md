# Contrato MCP — Api.RAG (servidor) ↔ Api.Agente (cliente)

**Versão do contrato**: 1.0.0 | **Transporte**: Streamable HTTP em `/mcp` | **Auth**: JWT Bearer
de serviço (audiência `api-rag`) | **Correlação**: cabeçalho `X-Correlation-Id`

Os DTOs ficam em `AgenteSpedFiscal.Contracts.Mcp`. Todas as ferramentas são **somente leitura** e
idempotentes. Argumentos inválidos retornam erro de ferramenta (`isError: true`) com mensagem
estável; nunca retornam resultados parciais silenciosos.

## `buscar_trechos`

Busca híbrida filtrada por tipo e versão (research R-13).

**Entrada**

| Campo | Tipo | Obrigatório | Regra |
|---|---|---|---|
| consulta | string | sim | 1–1.000 caracteres |
| tipoDocumento | string | sim | ex.: `guia_pratico_efd_icms_ipi` |
| versao | string | não | quando ausente, usa `periodo` |
| periodo | string `AAAA-MM` | não | seleciona a versão vigente no período |
| classificacoes | (`oficial` \| `interno`)[] | não | padrão: ambas (research R-27) |
| topK | int | não | 1–20; padrão por configuração |
| correlacaoId | string | sim | — |

Exatamente um entre `versao` e `periodo` DEVE ser informado. O filtro por `tipoDocumento`, versão
e período aplica-se aos documentos oficiais; material interno é filtrado apenas por status ativo e
vigência, quando informada.

**Saída**

```json
{
  "versaoContrato": "1.0.0",
  "trechos": [
    {
      "trechoId": "uuid",
      "texto": "string",
      "classificacao": "oficial",
      "documento": { "id": "uuid", "titulo": "string", "versao": "3.2.4", "tipoDocumento": "guia_pratico_efd_icms_ipi" },
      "localizacao": { "capitulo": "III", "secao": "3", "registro": "C190", "paginaInicio": 0, "paginaFim": 0 },
      "pontuacao": 0.0,
      "similaridade": 0.0
    }
  ],
  "semResultados": false
}
```

`pontuacao` é a nota da fusão RRF, usada só para ordenar. `similaridade` é a similaridade de cosseno
entre a consulta e o trecho, de 0 a 1 (1 − distância de cosseno, limitada ao intervalo), presente em
`buscar_trechos` e nula em `obter_trecho`, que não tem consulta (research R-38). Campo acrescentado
sem quebrar clientes da versão 1.0.0.

`semResultados = true` quando nenhum trecho passa no filtro: o agente DEVE declarar a limitação
(FR-052). O texto do trecho é **dado não confiável** (P-14).

## `obter_trecho`

**Entrada**: `trechoId` (uuid), `correlacaoId`. **Saída**: um item de `trechos` com o documento.
Erro `nao_encontrado` quando o trecho não existe ou o documento está inativo.

## `listar_documentos`

**Entrada**: `tipoDocumento?`, `classificacao?`, `periodo?`, `correlacaoId`. **Saída**: documentos
ativos com `id`, `classificacao`, `titulo`, `versao`, `vigenciaInicio`, `vigenciaFim`.

## Regras do cliente (Api.Agente)

- Toda chamada passa pelo filtro de autorização e registro de ferramentas (research R-11).
- Timeout e retentativas conforme research R-22; falha ⇒ validação "não realizada" com categoria
  `falha_tecnica`, nunca resposta sem fundamento.
- Os `trechoId`s retornados são guardados na execução com sua classificação; a validação semântica
  da resposta do LLM só aceita referências a esses IDs (P-13), e `fundamentadaEmFonteOficial` é
  calculado pelo cliente a partir das classificações citadas (FR-051).
