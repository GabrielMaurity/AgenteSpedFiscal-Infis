# ADR-0002: Embeddings e busca híbrida

**Status**: Aceito (parâmetros finais dependem dos evals, T106)
**Data**: 2026-11-05

## Contexto

Perguntas fiscais misturam linguagem natural com códigos exatos, como `C190` e `VL_OPR`. A
recuperação precisa respeitar versão e classificação (P-09), e os parâmetros não podem ser fixados
sem avaliação (P-10).

## Decisão

- Modelo candidato: `text-embedding-3-small`, 1.536 dimensões, coluna `vector(1536)` com índice HNSW
  e `vector_cosine_ops`.
- Busca híbrida: similaridade vetorial e busca textual do PostgreSQL, com dicionário `portuguese` e
  correspondência exata de códigos, combinadas por Reciprocal Rank Fusion.
- Filtros por status, tipo, versão ou período e classificação são aplicados antes do ranqueamento.

## Alternativas consideradas

- **Só vetorial**: recupera mal identificadores exatos.
- **Só textual**: perde paráfrases.
- **`text-embedding-3-large`** (3.072 dimensões): exige `halfvec` para HNSW; só se os evals mostrarem
  ganho relevante.

## Consequências

- Trocar o modelo ou a dimensão exige migração e reprocessamento da base.
- Tamanho de chunk e `topK` configurados são valores iniciais, não aprovados, até os evals.
