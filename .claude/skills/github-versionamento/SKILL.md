---
name: github-versionamento
description: Padronizar o versionamento Git e GitHub em projetos conduzidos com GitHub Spec Kit e SDD. Usar ao solicitar commit, push, abertura de pull request, criação de branch, publicação de feature ou sincronização de alterações do código e artefatos de especificação.
---

# GitHub: versionamento, commit e push

## Contrato de entrada e saída
- Entradas: repositório local, branch de destino, contexto da feature Spec Kit, permissões de publicação e convenções existentes.
- Saídas: alterações verificadas, commit rastreável, push confirmado, hash do commit, branch e URL do PR quando aplicável; informar bloqueios sem falsificar sucesso.

## Regras inegociáveis
1. Inspecionar `git status --short --branch`, `git remote -v`, `git branch --show-current`, `git diff --stat` e `git diff` antes de qualquer modificação.
2. Verificar branch protegida, estado de sincronização, commits anteriores e regras de contribuição; **não** forçar push nem executar reset destrutivo sem aprovação explícita.
3. Não adicionar segredos, `.env`, chaves privadas, credenciais, binários de build ou dados pessoais. Inspecionar arquivos staged (`git diff --cached`) e `.gitignore`; interromper se houver riscos.
4. Preservar mudanças locais preexistentes e nunca incluir automaticamente arquivos de outra tarefa. Preferir `git add` com caminhos explícitos em vez de `git add .`.
5. Relacionar implementação a `specs/<feature>/spec.md`, `plan.md`, `tasks.md`, contratos e testes existentes. Não inventar arquivos ausentes.
6. Antes do commit, executar as validações específicas do projeto (`dotnet test`, `dotnet build`, linter, testes de contrato etc.). Registrar falhas e não afirmar aprovação sem executar.
7. Mensagem de commit no padrão Conventional Commits: `feat(agente): implementar cliente MCP`, `fix(rag): corrigir filtro pgvector`, `docs(spec): atualizar contratos`.
8. Não publicar na `main` diretamente quando houver política de PR; criar `feat/<id>-<slug>` ou seguir padrão do repositório.
9. Commits e pushes são operações que mudam histórico remoto: cumprir a autorização disponível. Se faltar acesso ou autenticação, oferecer comandos precisos para execução pelo usuário, sem simular resultados.

## Fluxo operacional

### 1. Identificação e revisão
```powershell
git status --short --branch
git remote -v
git branch --show-current
git fetch origin
git log -5 --oneline
git diff --check
git diff --stat
```
Verificar se `origin` aponta para o repositório esperado e se existe divergência entre a branch local e a remota.

### 2. Build e testes
Para .NET:
```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```
Se não houver solução ou testes, identificar os projetos e adaptar os comandos; não mascarar falhas.

### 3. Seleção de arquivos e commit
```powershell
git add specs/ src/ tests/
git diff --cached --check
git diff --cached --stat
git diff --cached
git commit -m "feat(agente): implementar integracao A2A"
```
**Exemplo apenas:** adicionar só caminhos existentes e pertinentes, jamais presumir que `specs/ src/ tests/` existem.

### 4. Sincronização e publicação
```powershell
git fetch origin
git status -sb
git push -u origin HEAD
```
Se houver divergência, interromper e avaliar merge/rebase conforme política. Não reescrever histórico compartilhado. Quando necessário, abrir PR com `gh pr create --fill` após verificar `gh auth status` e o destino correto.

### 5. Evidências e conclusão
Apresentar resumo de alterações, execução real dos testes, branch local/remota, hash (`git rev-parse --short HEAD`), estado final (`git status -sb`) e link do PR se gerado. Se push falhar, distinguir commit local de publicação remota.

## Interface com Spec Kit
- Executar após concluir tasks e validações, respeitando a Constitution do repositório.
- Incluir mudanças em `.specify/` e `specs/` somente se pertinentes à feature e revisadas.
- Impedir commit de documentação que contradiga implementação ou contratos.
- Atualizar tarefas concluídas apenas com evidências reais; não marcar checklist por suposição.
