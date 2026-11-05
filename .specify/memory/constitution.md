# AgenteSpedFiscal Constitution

Constitution do projeto **AgenteSpedFiscal-Infis**. Este documento é normativo: prevalece sobre
especificações, planos, tarefas, implementações e revisões. As palavras **DEVE**, **NÃO DEVE** e
**PODE** indicam, respectivamente, obrigação, proibição e opção. Toda regra marcada com DEVE ou
NÃO DEVE é verificável em revisão de pull request (PR).

## 1. Propósito e escopo

### 1.1 Propósito

O AgenteSpedFiscal é um agente de IA que:

- valida arquivos SPED Fiscal quanto a estrutura, registros e regras aplicáveis;
- responde a perguntas e traz informações fundamentadas sobre SPED Fiscal a partir de uma base de
  informação com documentos oficiais e material interno, sempre classificados.

A solução combina validação determinística, consulta à base de informação por RAG, uso controlado
de ferramentas externas, análise com LLM e respostas estruturadas e rastreáveis, com integração
futura a outros agentes e orquestradores por A2A.

### 1.2 Escopo desta constitution

- Aplica-se a todos os projetos da solução AgenteSpedFiscal, incluindo `Api.Agente`, `Api.RAG`
  e quaisquer bibliotecas, testes, pipelines e documentos de especificação.
- Define princípios, restrições e critérios obrigatórios. NÃO define layouts, regras fiscais,
  schemas, versões de SDKs nem parâmetros ajustáveis; estes DEVEM constar das especificações,
  planos técnicos e ADRs.

### 1.3 Fora de escopo

- Esta constitution NÃO aprova nenhuma regra fiscal, layout ou critério de conformidade.
  Esses elementos DEVEM ser obtidos de documentos oficiais e registrados nas especificações
  (ver P-01).
- O agente NÃO substitui a responsabilidade legal do contribuinte ou do profissional contábil.
  Resultados DEVEM indicar quando revisão humana é necessária (ver P-11).
- O agente NÃO DEVE gerar, montar, corrigir ou reescrever arquivos SPED Fiscal, nem assinar ou
  transmitir arquivos. Incluir geração no escopo exige emenda MAJOR desta constitution com novo
  princípio próprio.

## 2. Princípios obrigatórios

Cada princípio apresenta **Regra**, **Justificativa** e **Verificação**.

### P-01 — Fidelidade às fontes oficiais

**Regra**

- Layouts, registros, campos, regras de validação, schemas e critérios de conformidade DEVEM ser
  obtidos de documentos oficiais identificados por origem, título e versão.
- Toda regra fiscal implementada DEVE referenciar, na especificação, o documento oficial, a versão
  e a localização (seção, registro, item ou página) de onde foi extraída.
- A equipe e o agente NÃO DEVEM inventar layouts, regras fiscais, schemas ou critérios de
  conformidade. Lacunas DEVEM ser registradas como decisão pendente ou esclarecimento.

**Justificativa**: erros fiscais têm consequência legal e financeira; regras sem fonte não são
auditáveis.

**Verificação**: em PR, toda regra de validação nova ou alterada possui referência à
fonte oficial na especificação; ausência de referência bloqueia a aprovação.

### P-02 — Validação determinística antes do LLM

**Regra**

- Regras estruturais e regras de negócio determinísticas DEVEM ser implementadas por validadores
  ou ferramentas verificáveis, com testes automatizados.
- O LLM NÃO DEVE substituir, contornar ou reinterpretar o resultado de uma validação
  determinística.
- A validação por XSD DEVE ser aplicada somente a documentos XML para os quais exista schema
  oficial correspondente. Documentos em outros formatos DEVEM usar validadores compatíveis com
  seus layouts.
- O resultado de validações determinísticas DEVE ser entregue ao LLM como dado de entrada, e
  NÃO DEVE ser alterado pela resposta do modelo.

**Justificativa**: regras determinísticas exigem resultados reprodutíveis; LLMs são
probabilísticos.

**Verificação**: para cada regra determinística existe validador com teste; a revisão confirma que
nenhum fluxo usa o LLM como única fonte de decisão para regra determinística; nenhum validador
XSD é aplicado a formato não XML.

### P-03 — Classificação explícita dos achados

**Regra**: todo achado retornado DEVE pertencer a exatamente uma das categorias:

1. **Erro estrutural** — violação de layout, formato, schema ou estrutura de registros.
2. **Inconsistência de negócio** — violação de regra de negócio determinística.
3. **Indício identificado pela IA** — observação do LLM, não comprovada deterministicamente.
4. **Ausência de evidências** — impossibilidade de concluir por falta de fonte ou dado.
5. **Falha técnica ou dependência indisponível** — erro de execução, timeout ou serviço
   indisponível.

- Indícios da IA NÃO DEVEM ser apresentados como erro estrutural ou inconsistência de negócio.
- Categorias 1 e 2 DEVEM ser originadas exclusivamente por validadores ou ferramentas
  determinísticas.

**Justificativa**: o usuário precisa distinguir fato comprovado, suspeita e limitação técnica.

**Verificação**: o contrato de resposta possui campo de categoria obrigatório; testes cobrem cada
categoria; testes garantem que achados das categorias 1 e 2 possuem origem determinística.

### P-04 — Nenhuma declaração de conformidade sem validação concluída

**Regra**

- O agente NÃO DEVE declarar um documento conforme quando qualquer validação obrigatória não tiver
  sido concluída, inclusive por falha técnica, dependência indisponível ou ausência de evidências.
- O status de processamento DEVE distinguir, no mínimo: processamento concluído, processamento
  concluído com limitações (validações não realizadas) e processamento com falha.
- Validações não realizadas DEVEM ser listadas explicitamente na resposta.

**Justificativa**: uma falsa declaração de conformidade é o erro mais grave possível do sistema.

**Verificação**: testes simulam indisponibilidade de cada dependência (RAG, LLM, ferramentas) e
confirmam que a resposta não declara conformidade e lista as validações não realizadas.

### P-05 — Fluxo de validação documental obrigatório

**Regra**: a validação de documentos DEVE seguir, nesta ordem:

1. Receber o arquivo e identificar usuário, contexto e execução.
2. Validar tamanho, formato e condições de processamento.
3. Identificar tipo de documento e versão do layout.
4. Validar estrutura, registros e schema aplicável.
5. Consultar os chunks do manual correspondente por MCP.
6. Executar ferramentas externas quando necessário e autorizado.
7. Produzir a análise com o LLM.
8. Validar a saída estruturada.
9. Retornar o resultado com evidências e rastreabilidade.

- A falha de uma etapa DEVE ser registrada com sua categoria (P-03) e NÃO DEVE ser omitida.
- Etapas 1 a 4 DEVEM interromper o fluxo quando a falha impedir as etapas seguintes; o retorno
  DEVE explicar o motivo.
- Uma especificação PODE acrescentar etapas, mas NÃO DEVE remover ou reordenar as etapas acima
  sem desvio aprovado (seção 8.3).

**Justificativa**: garante que o LLM receba somente documentos já identificados e validados, com
contexto oficial correto.

**Verificação**: o plano técnico mapeia cada etapa para um componente; testes de integração
cobrem o fluxo completo e a interrupção nas etapas 1 a 4; os registros de execução exibem cada
etapa com o mesmo identificador de execução.

### P-06 — (Revogado na versão 2.0.0)

Princípio de geração determinística de arquivos SPED revogado: a geração saiu do escopo do agente
(seção 1.3). O número é mantido para não alterar as referências P-07 a P-22 nas especificações.

### P-07 — Fronteiras de serviço e propriedade dos dados

**Regra**

- A comunicação funcional entre `Api.Agente` e `Api.RAG` DEVE ocorrer por MCP, com `Api.Agente`
  como cliente e `Api.RAG` como servidor.
- Cada serviço DEVE ser proprietário exclusivo dos seus dados.
- `Api.Agente` NÃO DEVE acessar diretamente tabelas, vetores, credenciais ou armazenamento interno
  da `Api.RAG`, nem o inverso.
- Os endpoints REST da `Api.RAG` destinam-se à administração da base de informação e NÃO DEVEM
  ser usados pela `Api.Agente` para recuperação funcional de conhecimento.

**Justificativa**: preserva autonomia, evolução independente e contratos explícitos entre
serviços.

**Verificação**: `Api.Agente` não possui connection string, referência de projeto ou dependência
de acesso a dados da `Api.RAG`; revisão de PR rejeita qualquer acoplamento de dados entre
serviços.

### P-08 — Arquitetura em camadas e simplicidade

**Regra**

- Cada serviço DEVE separar endpoints, aplicação, domínio e infraestrutura.
- O domínio NÃO DEVE depender de infraestrutura, frameworks web, SDKs de LLM ou bancos de dados.
- Projetos, bibliotecas, padrões ou dependências adicionais DEVEM ser justificados no plano
  técnico ou em ADR, indicando o problema resolvido e a alternativa mais simples descartada.

**Justificativa**: isola as regras fiscais de detalhes tecnológicos e evita complexidade sem
necessidade.

**Verificação**: as referências entre projetos respeitam a direção de dependência; todo projeto
ou pacote novo em PR tem justificativa rastreável.

### P-09 — Base de informação classificada, rastreável e versionada

**Regra**

- Todo documento ingerido DEVE possuir classificação (`oficial` ou `interno`), origem,
  identificação, versão e data de ingestão. Documentos sem classificação DEVEM ser recusados.
- Material interno NÃO DEVE ser usado como fonte de regra de validação (P-01) nem apresentado como
  norma ou fonte oficial.
- Todo chunk DEVE permitir recuperar documento de origem, classificação, versão e localização
  (seção, página ou registro), quando disponível.
- A ingestão DEVE definir critérios de segmentação, deduplicação, reprocessamento e atualização,
  registrados na especificação ou no plano técnico.
- A recuperação DEVE filtrar pelo tipo de documento e pela versão do manual aplicável ao arquivo
  analisado.
- Respostas fundamentadas no RAG DEVEM apresentar referências recuperáveis aos chunks utilizados,
  com a classificação de cada uma. Uma resposta apoiada apenas em material interno DEVE declarar
  que não há fundamento em fonte oficial recuperada.
- Na ausência de evidências suficientes, o agente DEVE declarar a limitação (categoria 4 de P-03),
  e NÃO DEVE responder como se houvesse fundamentação.

**Justificativa**: manuais fiscais mudam entre versões; usar a versão errada produz conclusões
erradas. Material interno é útil, mas confundi-lo com norma oficial gera risco fiscal.

**Verificação**: o modelo de dados do documento e do chunk contém os metadados exigidos, incluindo
a classificação; testes confirmam o filtro por tipo e versão, a recusa de ingestão sem
classificação e a sinalização de citações internas; testes confirmam que toda referência
retornada corresponde a um chunk existente.

### P-10 — Parâmetros de RAG especificados e avaliados

**Regra**

- Modelo de embeddings, dimensão dos vetores, métrica de distância, índice e estratégia de busca
  DEVEM ser compatíveis entre si e documentados no plano técnico ou em ADR.
- Tamanho dos chunks, sobreposição, topK e limiares de similaridade NÃO DEVEM ser fixados sem
  avaliação. Eles DEVEM ser especificados e avaliados com conjunto de exemplos representativos,
  com resultados registrados.
- A troca do modelo de embeddings ou da dimensão DEVE prever reprocessamento da base e NÃO DEVE
  misturar vetores incompatíveis na mesma busca.

**Justificativa**: parâmetros arbitrários degradam a recuperação de forma silenciosa.

**Verificação**: existe conjunto de avaliação versionado com perguntas e fontes esperadas; o plano
registra métricas obtidas; alterações de parâmetros em PR incluem nova avaliação.

### P-11 — Saída estruturada versionada e validada

**Regra**

- A resposta do LLM DEVE seguir contrato JSON explícito e versionado.
- A integração DEVE usar o mecanismo de saída estruturada compatível com o modelo configurado.
- A resposta DEVE ser validada estrutural e semanticamente antes de aceita. Respostas inválidas
  DEVEM seguir política de retentativas limitada, definida no plano técnico; esgotadas as
  tentativas, o resultado DEVE ser registrado como falha técnica (P-03), sem conteúdo inválido.
- O contrato DEVE contemplar, conforme a especificação: identificador da execução, status do
  processamento, resultado da análise, inconsistências com severidade, evidências e referências,
  limitações e validações não realizadas, e necessidade de revisão humana.

**Justificativa**: só é possível integrar, auditar e testar respostas com estrutura garantida.

**Verificação**: existe schema versionado do contrato; testes cobrem resposta válida, inválida
estruturalmente, inválida semanticamente e esgotamento de retentativas.

### P-12 — Separação entre conteúdo do LLM e metadados do sistema

**Regra**

- O JSON produzido pelo LLM DEVE ser distinto do envelope retornado pela API.
- Identificador da execução, identificadores de correlação, consumo de tokens, custos, tempos,
  versões e demais metadados operacionais DEVEM ser preenchidos pelo sistema.
- Valores desses campos eventualmente gerados pelo modelo NÃO DEVEM ser aceitos; o sistema DEVE
  sobrescrevê-los ou descartá-los.

**Justificativa**: o modelo não conhece metadados operacionais e pode inventá-los.

**Verificação**: os contratos do LLM e do envelope são tipos separados; testes confirmam que
metadados vêm do sistema mesmo quando o modelo retorna valores divergentes.

### P-13 — Proibição de fabricação

**Regra**

- O agente NÃO DEVE fabricar evidências, resultados de ferramentas, referências, trechos de
  manuais ou resultados de validação.
- Toda referência citada na resposta DEVE corresponder a um chunk efetivamente recuperado na
  execução; todo resultado de ferramenta citado DEVE corresponder a uma execução registrada.
- Referências ou resultados não correspondentes DEVEM invalidar a resposta na validação semântica
  (P-11).

**Justificativa**: evidência fabricada destrói a confiança e a utilidade do sistema.

**Verificação**: a validação semântica cruza referências e resultados citados com os registros da
execução; testes cobrem referência inexistente.

### P-14 — Conteúdo externo é dado não confiável

**Regra**

- Conteúdo de arquivos enviados, chunks, respostas de ferramentas, mensagens A2A e histórico
  conversacional DEVE ser tratado como dado não confiável.
- Esse conteúdo NÃO DEVE alterar instruções do sistema, regras de validação, autorizações,
  ferramentas disponíveis nem o contrato de saída.
- Instruções de sistema e conteúdo não confiável DEVEM ser separados de forma explícita no
  prompt.

**Justificativa**: arquivos e manuais podem conter texto que tenta manipular o modelo (prompt
injection).

**Verificação**: existem testes com conteúdo malicioso em arquivo e em chunk demonstrando que
regras, autorizações e contrato não mudam.

### P-15 — Ferramentas governadas

**Regra**: cada ferramenta exposta ao LLM (Semantic Kernel/Function Calling) ou por MCP DEVE
possuir:

- nome e responsabilidade claros e únicos;
- contratos de entrada e saída;
- validação de argumentos antes da execução;
- regras de autenticação e autorização;
- timeout e tratamento de falhas;
- política de retentativa e de idempotência, quando aplicável;
- testes automatizados e registro de execução.

Além disso:

- A seleção de uma ferramenta pelo LLM NÃO DEVE conceder autorização para executá-la; a
  autorização DEVE ser verificada pelo sistema com base no usuário, contexto e execução.
- Ferramentas com efeitos colaterais externos DEVEM ser identificadas como tal na especificação.

**Justificativa**: o LLM decide de forma probabilística; o controle de acesso precisa ser
determinístico.

**Verificação**: o catálogo de ferramentas na especificação contém todos os itens acima; testes
demonstram que chamada não autorizada é recusada mesmo quando solicitada pelo LLM.

### P-16 — Memória conversacional isolada e governada

**Regra**

- A memória conversacional DEVE ser persistida em MongoDB, de propriedade da `Api.Agente`
  (D-01, resolvida). A decisão DEVE estar registrada em ADR antes da implementação.
- A memória DEVE ser isolada por usuário e conversa, e por organização quando aplicável.
- A memória DEVE ter política de retenção, expiração e exclusão definida na especificação.
- O histórico enviado ao modelo DEVE ter limite configurado.
- Dados sensíveis DEVEM ser protegidos conforme a seção 5.
- Histórico conversacional NÃO DEVE ser tratado como conhecimento oficial nem como fonte de
  referência fiscal.

**Justificativa**: evita vazamento entre usuários e evita que conversas contaminem a base oficial.

**Verificação**: testes demonstram que um usuário não lê a memória de outro; a política de
retenção está especificada e testada; respostas não citam histórico como fonte oficial.

### P-17 — Consumo e custos mensuráveis e honestos

**Regra**

- O consumo DEVE ser persistido em PostgreSQL, de propriedade da `Api.Agente`.
- Cada chamada a provedor de IA, inclusive chamadas adicionais e retentativas, DEVE gerar
  registro com, quando disponibilizado pelo provedor: execução e correlação, provedor e modelo,
  tokens de entrada e saída, consumo de embeddings, data, duração, status, estimativa de custo e
  referência da tabela de preços utilizada.
- Valores estimados DEVEM ser distinguidos de valores informados pelo provedor.
- Consumo desconhecido NÃO DEVE ser registrado como zero; DEVE ser registrado como desconhecido.
- O consumo de embeddings na `Api.RAG` DEVE ser registrado pela própria `Api.RAG`; a forma de
  consolidação com a `Api.Agente` é decisão pendente (D-11).

**Justificativa**: custo de IA precisa ser auditável e não pode ser subestimado.

**Verificação**: testes cobrem retentativa (gera registros), consumo desconhecido (não vira zero)
e marcação estimado/informado.

### P-18 — Rastreabilidade e observabilidade ponta a ponta

**Regra**

- Toda execução DEVE ter identificador único gerado pelo sistema, propagado por API, MCP,
  ferramentas, chamadas ao LLM, A2A e registros de consumo.
- Logs DEVEM ser estruturados e correlacionados pelo identificador da execução.
- Logs NÃO DEVEM conter segredos nem o conteúdo integral de arquivos fiscais ou dados pessoais
  sem proteção.
- O resultado DEVE permitir reconstruir quais validações, chunks, ferramentas e modelo foram
  usados.

**Justificativa**: auditoria fiscal e diagnóstico de falhas exigem reconstrução da execução.

**Verificação**: testes de integração confirmam a propagação do identificador entre serviços; a
revisão de PR verifica ausência de segredos e conteúdo sensível em logs.

### P-19 — Configuração externa

**Regra**

- Modelo, parâmetros de execução, endpoints, limites, timeouts, retentativas, parâmetros de RAG e
  tabelas de preços DEVEM ser configuráveis externamente ao código.
- Segredos DEVEM ser fornecidos por mecanismo seguro de configuração e NÃO DEVEM ser versionados
  no repositório.
- A configuração DEVE ser validada na inicialização; configuração inválida DEVE impedir o início
  do serviço.

**Justificativa**: modelos e preços mudam; a troca não pode exigir alteração de código.

**Verificação**: busca no repositório não encontra segredos; testes de inicialização cobrem
configuração ausente ou inválida.

### P-20 — Interoperabilidade por A2A

**Regra**

- O agente DEVE ser exposto por servidor A2A na `Api.Agente`.
- As capacidades anunciadas por A2A DEVEM corresponder às capacidades implementadas e testadas.
- Mensagens A2A DEVEM passar pelas mesmas regras de autenticação, autorização, validação, fluxo
  (P-05) e contrato de saída (P-11) que a API REST.
- O A2A NÃO DEVE oferecer caminho que contorne validações determinísticas.

**Justificativa**: outros agentes e orquestradores precisam do mesmo nível de garantia que
usuários diretos.

**Verificação**: testes de contrato A2A; teste demonstra que a mesma entrada via REST e via A2A
produz resultado equivalente.

### P-21 — Especificação antes da implementação

**Regra**

- Nenhuma funcionalidade DEVE ser implementada sem especificação aprovada, plano técnico e
  tarefas, conforme a seção 6.
- Cada requisito DEVE ter critério de aceite verificável e rastreabilidade para tarefas e testes.
- Decisões arquiteturais relevantes DEVEM ser registradas em ADR.
- Suposições NÃO DEVEM ser tratadas como requisitos aprovados; DEVEM ser registradas como
  esclarecimento ou decisão pendente.

**Justificativa**: o domínio fiscal exige rastreabilidade entre regra, código e teste.

**Verificação**: todo PR referencia especificação e tarefas; a matriz requisito → tarefa → teste
está completa para a funcionalidade.

### P-22 — Testes como evidência

**Regra**

- Validadores, ferramentas, contratos (REST, MCP, A2A, saída do LLM) e regras de
  segurança DEVEM ter testes automatizados.
- Regras fiscais DEVEM ter testes com exemplos positivos e negativos derivados da fonte oficial.
- Testes que dependam de LLM ou provedores externos DEVEM ser isolados de testes determinísticos;
  a suíte determinística NÃO DEVE depender de chamadas reais ao provedor.
- Avaliações de qualidade do LLM e do RAG DEVEM usar conjuntos de avaliação versionados.

**Justificativa**: em sistema fiscal, teste é a evidência de que a regra foi implementada como
especificado.

**Verificação**: pipeline executa a suíte determinística em todo PR; cobertura de critérios de
aceite verificada pela matriz de rastreabilidade.

## 3. Restrições arquiteturais

### 3.1 Plataforma

- A solução DEVE se chamar **AgenteSpedFiscal** e DEVE utilizar **.NET 10** com **ASP.NET Core**.
- A integração com LLM DEVE ser feita com a **OpenAI**.
- A integração e a execução de ferramentas por Function Calling na `Api.Agente` DEVEM utilizar
  **Semantic Kernel**.
- Vetores DEVEM ser armazenados e consultados em **PostgreSQL com PgVector**.
- O consumo e os custos DEVEM ser persistidos em **PostgreSQL**.
- A memória conversacional DEVE utilizar **MongoDB** (D-01, resolvida; registro em ADR).
- Versões de SDKs, pacotes e parâmetros ajustáveis DEVEM constar dos planos técnicos, e NÃO
  DEVEM ser fixados nesta constitution.

### 3.2 Api.Agente

Responsabilidades obrigatórias:

- API REST para execução do agente;
- recebimento de arquivos e solicitações de análise;
- orquestração do fluxo de validação (P-05) e das consultas à base de informação;
- integração com a OpenAI;
- consulta à `Api.RAG` como cliente MCP;
- execução de ferramentas por Semantic Kernel (P-15);
- gestão de memória conversacional (P-16);
- registro de consumo e custos (P-17);
- validação da resposta estruturada do LLM (P-11);
- exposição do agente por servidor A2A (P-20).

### 3.3 Api.RAG

Responsabilidades obrigatórias:

- API REST para administração da base de informação (documentos oficiais e material interno,
  com classificação obrigatória, P-09);
- endpoint POST de ingestão;
- processamento de documentos, geração de chunks e embeddings;
- armazenamento e consulta de vetores em PostgreSQL com PgVector;
- exposição das ferramentas de recuperação de conhecimento por servidor MCP.

A `Api.RAG` NÃO DEVE chamar o LLM de análise nem conter regras de validação fiscal; sua função é
gerir e recuperar conhecimento.

### 3.4 Estrutura interna

- Cada serviço DEVE separar as camadas de endpoints, aplicação, domínio e infraestrutura (P-08).
- Bibliotecas compartilhadas entre serviços DEVEM se limitar a contratos e utilitários sem estado;
  NÃO DEVEM compartilhar acesso a dados.
- Projetos adicionais DEVEM ser justificados no plano técnico ou em ADR.

## 4. Contratos e governança dos dados

### 4.1 Contratos obrigatórios

Os seguintes contratos DEVEM ser explícitos, versionados e cobertos por testes de contrato:

| Contrato | Proprietário | Conteúdo mínimo |
|---|---|---|
| API REST da `Api.Agente` | Api.Agente | requisição de validação e de consulta; envelope de resposta |
| Envelope de resposta | Api.Agente | metadados do sistema (P-12) + resultado do LLM validado |
| Resposta do LLM | Api.Agente | campos de P-11 |
| Ferramentas MCP da `Api.RAG` | Api.RAG | consulta filtrável por tipo/versão; chunks com referência |
| API REST da `Api.RAG` | Api.RAG | ingestão e administração da base |
| Ferramentas do Semantic Kernel | Api.Agente | itens de P-15 |
| Agente A2A | Api.Agente | capacidades anunciadas e formato das mensagens |
| Registro de consumo | Api.Agente | campos de P-17 |

### 4.2 Versionamento de contratos

- Contratos DEVEM seguir versionamento semântico.
- Mudanças incompatíveis DEVEM incrementar a versão principal e DEVEM ter plano de migração.
- O envelope de resposta DEVE informar a versão do contrato utilizada.

### 4.3 Dados

- Cada serviço DEVE ser proprietário dos seus bancos, schemas e vetores (P-07).
- Documentos da base de informação, chunks, histórico conversacional, registros de consumo,
  resultados de validação e arquivos enviados DEVEM ter política de retenção definida em
  especificação ou plano técnico (D-08).
- Arquivos fiscais e dados pessoais DEVEM ser tratados conforme a LGPD e a seção 5.
- Base de informação e histórico conversacional DEVEM permanecer separados (P-16).
- Alterações de schema de dados DEVEM ser versionadas por migrações.

## 5. Segurança e qualidade

### 5.1 Segurança

- Todos os endpoints (REST, MCP e A2A) DEVEM exigir autenticação, exceto endpoints de saúde
  explicitamente justificados.
- A autorização DEVE ser verificada pelo sistema para cada operação e cada ferramenta (P-15).
- O mecanismo de autenticação entre serviços e com clientes é decisão pendente (D-07).
- Uploads DEVEM ter limites de tamanho, tipo e formato validados antes do processamento (P-05).
- Segredos NÃO DEVEM ser versionados nem registrados em logs (P-19).
- Dados sensíveis (por exemplo, CNPJ, CPF, valores fiscais) DEVEM ser protegidos em trânsito e em
  repouso e minimizados nos logs, na memória e nos prompts.
- O envio de dados ao provedor de LLM DEVE se limitar ao necessário para a análise.
- Conteúdo externo DEVE ser tratado como não confiável (P-14).
- Dependências com vulnerabilidades conhecidas de severidade alta ou crítica NÃO DEVEM ser
  integradas sem desvio aprovado.

### 5.2 Qualidade

- O build DEVE ser concluído sem erros em todo PR.
- A suíte determinística de testes DEVE passar em todo PR (P-22).
- Contratos alterados DEVEM ter testes de contrato atualizados.
- Mudanças em prompts, modelo ou parâmetros de RAG DEVEM ser acompanhadas de execução do conjunto
  de avaliação e registro dos resultados.
- Timeouts, retentativas e tratamento de falhas DEVEM existir em toda chamada a dependência
  externa.
- Ferramentas de análise estática e formatação PODEM ser definidas no plano técnico; quando
  definidas, DEVEM ser executadas no pipeline.

## 6. Fluxo de desenvolvimento SDD

### 6.1 Etapas

O fluxo obrigatório é:

**constitution → especificação → esclarecimentos → plano técnico → tarefas → implementação →
validação**

- **Especificação**: requisitos, critérios de aceite verificáveis, fontes oficiais (P-01),
  categorias de achados afetadas (P-03) e decisões pendentes.
- **Esclarecimentos**: resolver ambiguidades antes do plano; suposições não resolvidas NÃO DEVEM
  seguir como requisitos.
- **Plano técnico**: componentes, contratos, parâmetros, dependências, versões e verificação de
  conformidade com esta constitution (Constitution Check).
- **Tarefas**: cada tarefa DEVE referenciar os requisitos que atende e os testes que a comprovam.
- **Implementação**: somente tarefas aprovadas.
- **Validação**: critérios de aceite comprovados por testes ou evidências registradas.

### 6.2 Rastreabilidade

- Cada funcionalidade DEVE manter matriz requisito → tarefa → teste.
- Cada PR DEVE referenciar a especificação e as tarefas atendidas.

### 6.3 ADRs

- Decisões arquiteturais relevantes DEVEM ser registradas em ADR em `docs/adr/`, com contexto,
  decisão, alternativas, consequências, data e status.
- São relevantes, no mínimo: escolha de tecnologia de persistência, mudança de contrato
  incompatível, adoção de novo projeto ou biblioteca estrutural, estratégia de autenticação,
  estratégia de busca e modelo de embeddings.

### 6.4 Revisão de PR

Todo PR DEVE ser revisado quanto a:

- conformidade com os princípios P-01 a P-22 aplicáveis (P-06 revogado);
- rastreabilidade com especificação e tarefas;
- itens da Definition of Done (seção 7);
- existência de desvio aprovado para qualquer regra não cumprida.

## 7. Definition of Done

Uma funcionalidade ou PR só está concluído quando:

- [ ] Especificação aprovada, com critérios de aceite verificáveis e fontes oficiais referenciadas.
- [ ] Esclarecimentos resolvidos ou registrados como decisões pendentes que não bloqueiam a entrega.
- [ ] Plano técnico com Constitution Check aprovado e ADRs necessários registrados.
- [ ] Tarefas rastreáveis a requisitos e testes.
- [ ] Build sem erros e suíte determinística aprovada.
- [ ] Regras determinísticas implementadas por validadores com testes positivos e negativos.
- [ ] Achados classificados nas categorias de P-03; nenhuma declaração de conformidade com
      validação pendente (P-04).
- [ ] Contratos versionados e testes de contrato atualizados.
- [ ] Saída do LLM validada estrutural e semanticamente; metadados preenchidos pelo sistema.
- [ ] Referências e resultados de ferramentas verificáveis; nada fabricado.
- [ ] Ferramentas com autorização, timeout, tratamento de falhas e testes.
- [ ] Consumo registrado conforme P-17, incluindo retentativas.
- [ ] Identificador de execução propagado e logs sem segredos ou dados sensíveis expostos.
- [ ] Configuração externa e sem segredos versionados.
- [ ] Avaliação de RAG/LLM executada quando prompts, modelos ou parâmetros mudarem.
- [ ] Documentação e matriz de rastreabilidade atualizadas.
- [ ] Desvios, se houver, registrados e aprovados (seção 8.3).

## 8. Governança e alterações

### 8.1 Supremacia

Esta constitution prevalece sobre especificações, planos, tarefas e práticas do projeto. Em caso
de conflito, a constitution DEVE ser seguida ou um desvio DEVE ser aprovado.

### 8.2 Alterações

- Alterações DEVEM ser feitas por PR que contenha: versão nova, data, motivação e avaliação de
  impacto nas especificações, planos e tarefas existentes.
- O versionamento segue SemVer:
  - **MAJOR**: remoção ou redefinição incompatível de princípio ou regra de governança;
  - **MINOR**: novo princípio ou seção, ou expansão material de regra;
  - **PATCH**: esclarecimento, redação ou correção sem mudança semântica.
- Especificações afetadas por alteração MAJOR ou MINOR DEVEM ser revisadas e ter a revisão
  registrada.
- Os papéis que aprovam alterações são decisão pendente (D-20).

### 8.3 Desvios

- Todo desvio DEVE ser registrado em `docs/desvios/` com: regra afetada, justificativa, impacto,
  responsável, aprovação, data e, quando temporário, prazo de revisão.
- Desvios temporários vencidos DEVEM ser reavaliados; NÃO DEVEM ser renovados tacitamente.
- Desvios NÃO DEVEM ser aprovados para P-04 (declaração de conformidade sem validação) e P-13
  (fabricação).

### 8.4 Verificação de conformidade

- Todo PR DEVE ser revisado conforme a seção 6.4.
- O Constitution Check do plano técnico DEVE ser reavaliado após o desenho e antes das tarefas.
- A conformidade da base de código com esta constitution DEVE ser revista a cada alteração MAJOR
  ou MINOR desta constitution.

## 9. Decisões pendentes

Itens abaixo NÃO são requisitos aprovados. Cada um DEVE ser resolvido na etapa indicada.

| ID | Decisão pendente | Etapa de resolução |
|---|---|---|
| D-01 | ~~MongoDB ou Redis para memória conversacional~~ **Resolvida em 1.1.0: MongoDB** | ADR de registro antes da implementação |
| D-02 | Modelos da OpenAI para análise e embeddings; dimensão dos vetores | Plano técnico + ADR |
| D-03 | ~~Inventário de fontes oficiais, tipos de SPED e versões de layout suportados~~ **Resolvida na spec 001: EFD ICMS/IPI, leiaute vigente, períodos a partir de 01/2026; Guia Prático 3.2.4 recebido; Nota Técnica do Ato COTEPE/ICMS nº 44/2018 pendente de obtenção** | Novas versões por especificação |
| D-04 | ~~Escopo da produção de SPED Fiscal~~ **Resolvida em 2.0.0: geração fora do escopo (seção 1.3)** | — |
| D-05 | ~~Limites de tamanho, formatos e codificações aceitos~~ **Resolvida na spec 001: `.txt` delimitado por pipe, até 500 MB configurável; codificação conforme Nota Técnica** | — |
| D-06 | Segmentação, sobreposição, topK, limiares e estratégia de busca | Plano técnico, após avaliação (P-10) |
| D-07 | Autenticação e autorização (clientes, REST, MCP, A2A) e multi-organização | ADR antes da primeira API exposta |
| D-08 | Retenção, expiração e exclusão de arquivos, memória, logs e consumo. **Parcial (spec 001): arquivo descartado ao fim do processamento; hash, metadados e resultado por 90 dias com exclusão sob demanda.** Pendentes: memória, logs e consumo | Plano técnico, com avaliação LGPD |
| D-09 | Catálogo de ferramentas externas e efeitos colaterais. **Spec 001: somente ferramentas de leitura, sem consulta externa durante a validação** | Especificação de cada funcionalidade |
| D-10 | ~~Taxonomia de severidades e critérios de revisão humana~~ **Resolvida na spec 001: impeditiva, advertência e informativa; critérios de revisão humana no FR-046** | — |
| D-11 | Consolidação do consumo de embeddings da Api.RAG com a Api.Agente | Plano técnico + ADR |
| D-12 | Fonte e atualização da tabela de preços | Plano técnico |
| D-13 | Limites de retentativa e timeouts | Plano técnico |
| D-14 | ~~Armazenamento ou descarte dos arquivos enviados~~ **Resolvida na spec 001: descarte ao fim do processamento** | — |
| D-15 | Versão do protocolo A2A, capacidades anunciadas e autenticação | Plano técnico + ADR |
| D-16 | Transporte e versão do protocolo MCP | Plano técnico + ADR |
| D-17 | Separação física dos dados PostgreSQL entre serviços | ADR |
| D-18 | Stack de observabilidade e formato de logs | Plano técnico |
| D-19 | Frameworks de testes e de avaliação de LLM/RAG | Plano técnico |
| D-20 | Papéis responsáveis por aprovar alterações e desvios | Governança, antes da primeira emenda |

**Version**: 2.0.0 | **Ratified**: 2026-10-29 | **Last Amended**: 2026-10-29
