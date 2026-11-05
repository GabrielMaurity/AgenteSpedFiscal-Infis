# ADR-0007: Separação dos bancos PostgreSQL

**Status**: Aceito
**Data**: 2026-11-05

## Contexto

Cada serviço é proprietário dos seus dados (P-07), e a constitution (D-17) deixou em aberto a
separação física.

## Decisão

Bancos lógicos `agente` e `rag`, com usuários distintos. Cada API recebe só a própria connection
string, e a extensão `vector` existe só no banco `rag`. Em produção, PODEM ser instâncias separadas.

## Alternativas consideradas

- **Banco único com schemas**: credenciais compartilhadas tornam a fronteira só uma convenção.
- **Instâncias separadas em desenvolvimento**: custo sem ganho.

## Consequências

- Não há consultas nem transações entre os serviços.
- Relatórios consolidados de consumo ficam para uma feature futura.
