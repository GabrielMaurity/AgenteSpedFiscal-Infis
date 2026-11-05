# ADR-0001: MongoDB para execuções e memória conversacional

**Status**: Aceito
**Data**: 2026-11-05

## Contexto

A constitution (D-01) pedia a escolha entre MongoDB e Redis para a memória conversacional, e a spec
exige manter resultados de validação por 90 dias com exclusão automática (FR-082).

## Decisão

Usar MongoDB na `Api.Agente`, com as coleções `execucoes` e `conversas`, ambas com índice TTL em
`expiraEm`. Toda consulta filtra por `orgId` (FR-080).

## Alternativas consideradas

- **Redis**: bom para histórico curto, mas ruim para resultados grandes e consultas por organização.
- **PostgreSQL para resultados**: exigiria um job próprio de expiração.

## Consequências

- A expiração é feita pelo próprio banco, com atraso de até cerca de 1 minuto.
- A `Api.Agente` passa a operar dois bancos: MongoDB e PostgreSQL (consumo e referências).
