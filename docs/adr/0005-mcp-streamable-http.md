# ADR-0005: MCP com Streamable HTTP

**Status**: Aceito
**Data**: 2026-11-05

## Contexto

A comunicação funcional entre `Api.Agente` e `Api.RAG` deve ocorrer por MCP (P-07), entre containers.

## Decisão

Usar o SDK oficial C# (`ModelContextProtocol` e `ModelContextProtocol.AspNetCore`) com transporte
Streamable HTTP em `/mcp`, token Bearer de serviço e `X-Correlation-Id`. Os DTOs ficam em
`AgenteSpedFiscal.Contracts.Mcp`.

## Alternativas consideradas

- **stdio**: só funciona no mesmo host.
- **REST direto**: proibido para uso funcional pela P-07.

## Consequências

- Timeout, retentativas e circuit breaker vêm do pipeline de resiliência da `Api.Agente`.
- Se a hospedagem futura não mantiver afinidade entre instâncias, o servidor precisará operar sem
  estado de sessão.
