# Arquivos de exemplo para teste manual

Dados **fictícios**: empresa, CNPJs, chaves de acesso e notas não existem. Use para testar
`POST /api/v1/validacoes` pelo Swagger local ou pelo Postman.

## sped-ficticio-com-erros-2026-01.txt

EFD ICMS/IPI de janeiro/2026 (leiaute 020, perfil A, RJ), gerado a partir de
`tests/fixtures/efd/ficticio-completo-2026-01.txt` com erros propositais. Codificação ISO-8859-1,
fim de linha CRLF, 94 linhas.

| # | Linha | Erro proposital | Achado esperado |
|---|---|---|---|
| 1 | 21 | NF 4501: `VL_DOC` = 4.800,00, mas os C190 somam 4.500,00 | **EFD-NEG-005** (VL_DOC × C190) |
| 2 | 22 | Item PROD001 movimentado em `CX`, unidade sem 0190 | **EFD-NEG-001** (unidade não cadastrada) |
| 2 | 22 | Mesmo item: unidade de estoque é `UN` e não há 0220 | **EFD-NEG-002** (fator de conversão) |
| 3 | 24 | C190 da NF 4501 com ICMS 504,00, mas os itens somam 540,00 | **EFD-NEG-007** (C190 × C170) e **EFD-NEG-004** (C100 × C190) |
| 4 | 25 | NF 7801: `VL_MERC` = 2.300,00, mas os C170 somam 2.200,00 | **EFD-NEG-006** (VL_MERC × C170) |
| 5 | 30 | NF 7815 com data de emissão 32/01/2026 (inexistente) | Nenhuma regra cobre: serve para ver se a análise de IA aponta |
| 6 | 38 | Numeração própria 1001, 1002, 1006 (faltam 1003 a 1005) | EFD-NEG-008 está fora do alcance (pendente de fonte oficial): aparece em `foraDoAlcance` |
| 7 | 70 | 9900 informa 6 registros C170, o arquivo tem 5 | **EFD-ESTR-016** (totalizador 9900) |

Resultado do motor local (sem IA): **7 achados**, status `concluida_com_limitacoes`, com
EFD-NEG-003 (NCM) em `validacoesNaoRealizadas` enquanto a tabela NCM não for importada.
