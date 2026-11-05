# Especificação Técnica: Montagem e Validação da EFD ICMS/IPI (SPED Fiscal)

Esta especificação técnica orienta a geração, montagem, estrutura e validação do arquivo digital da **Escrituração Fiscal Digital (EFD ICMS/IPI)**, popularmente denominada **SPED Fiscal**, no âmbito do Sistema Público de Escrituração Digital [35, 93].

---

## 1. Visão Geral e Finalidade Legal

A EFD ICMS/IPI constitui uma obrigação acessória em formato digital composta por um conjunto de registros de apuração de impostos e documentos fiscais referentes às operações com mercadorias e prestações de serviços praticadas pelo contribuinte [44, 78, 98, 99].

* **Substituição de Livros Físicos**: A EFD ICMS/IPI substitui a escrituração em papel dos livros de Registro de Entradas, Registro de Saídas, Registro de Inventário, Registro de Apuração do ICMS, Registro de Apuração do IPI, Controle do Crédito de ICMS do Ativo Permanente (CIAP) e o Controle da Produção e do Estoque (Bloco K) [78].
* **Periodicidade e Abrangência**: O arquivo digital deve ser gerado mensalmente e conter a totalidade das operações do estabelecimento do primeiro ao último dia do mês civil [99, 108].
* **Guarda dos Arquivos**: O contribuinte é obrigado a manter o arquivo `.txt` original gerado e transmitido, juntamente com os documentos fiscais que lhe deram origem, pelo prazo estabelecido na legislação tributária para fiscalização [99, 110].

---

## 2. Dados Técnicos de Geração e Estrutura do Arquivo

O arquivo digital da EFD ICMS/IPI possui leiaute padronizado em formato texto (`.TXT`), com campos delimitados pelo caractere pipe (`|`) [82, 91].

### 2.1. Organização por Blocos e Registros

O arquivo é organizado hierarquicamente em blocos sequenciais, sendo que cada bloco inicia com um registro de abertura e finaliza com um registro de encerramento [10, 81, 120].

| Bloco | Descrição do Bloco | Registros Principais e Função |
| :--- | :--- | :--- |
| **0** | Abertura, Identificação e Referências | `0000` (Abertura), `0005` (Dados Complementares), `0150` (Tabela de Participantes), `0190` (Unidades de Medida), `0200` (Tabela de Itens/Produtos), `0220` (Fatores de Conversão) [11, 81, 129]. |
| **B** | Escrituração e Apuração do ISS | `B001` (Abertura), `B020` (Notas de Serviços), `B470` (Apuração do ISS). Exclusivo para contribuintes do Distrito Federal [81, 146, 156]. |
| **C** | Documentos Fiscais I - Mercadorias (ICMS/IPI) | `C100` (Nota Fiscal modelo 01/1A/55/65), `C170` (Itens do Documento), `C190` (Resumo Analítico), `C400`/`C405` (ECF), `C800` (CF-e-SAT modelo 59) [81, 163, 172, 198, 257]. |
| **D** | Documentos Fiscais II - Serviços (ICMS) | `D100` (CT-e modelo 57, BP-e 63, CT-e OS 67), `D190` (Registro Analítico), `D700`/`D730` (NFCom modelo 62) [81, 274, 288, 329, 334]. |
| **E** | Apuração do ICMS e do IPI | `E100`/`E110` (Apuração do ICMS Próprio), `E200`/`E210` (Apuração do ICMS ST), `E300`/`E310` (DIFAL / FCP EC 87/15), `E500`/`E520` (Apuração do IPI) [81, 340, 360, 387, 408]. |
| **G** | Controle do Crédito de ICMS do Ativo Permanente (CIAP) | `G110` (Demonstrativo do CIAP), `G125` (Movimentação do Bem) [81, 416, 417]. |
| **H** | Inventário Físico | `H005` (Prazos do Inventário), `H010` (Itens do Inventário e Valoração) [81, 423, 424]. |
| **K** | Controle da Produção e do Estoque | `K100` (Período de Apuração), `K200` (Estoque Escriturado), `K220` (Movimentação Interna), `K230`/`K250` (Ordens de Produção) [81, 426, 427, 432]. |
| **1** | Outras Informações | `1010` (Obrigatoriedade de Registros), `1200`/`1210` (Controle de Créditos Acumulados), `1601` (Operações com Meios Eletrônicos de Pagamento) [81, 126, 133, 434, 437]. |
| **9** | Controle e Encerramento | `9001` (Abertura do Bloco 9), `9900` (Registros do Arquivo), `9999` (Encerramento do Arquivo Digital) [11, 81]. |

### 2.2. Perfis de Apresentação do Declarante

O enquadramento do perfil é estabelecido pela Secretaria da Fazenda de domicílio fiscal do contribuinte e informado no campo `IND_PERFIL` do Registro `0000` [137]:
* **Perfil A**: Apresentação analítica completa dos documentos fiscais e seus respectivos itens de mercadorias/serviços [121, 137].
* **Perfil B**: Apresentação sintética/consolidada (resumos diários ou mensais de documentos) [121, 137].
* **Perfil C**: Apresentação simplificada ou reduzida conforme regulamentação estadual específica [137].

---

## 3. Regras de Montagem e Consistência de Dados

### 3.1. Estrutura Obrigatória e Relacionamento Pai-Filho
1. **Pai e Filho**: Para a inclusão de um registro dependente ("Filho"), é indispensável a existência do registro principal ("Pai") correspondente [121].
2. **Abertura e Fechamento de Blocos**: Todos os blocos listados na especificação devem conter seus registros de abertura (ex.: `C001`, `D001`, `E001`) e fechamento (ex.: `C990`, `D990`, `E990`) [158, 272, 273, 483].
3. **Blocos sem Movimento**: Quando não houver dados a serem informados em determinado bloco no mês, o campo `IND_MOV` do registro de abertura deve ser preenchido com `1` ("Bloco sem dados informados"), mantendo-se apenas os registros de abertura e fechamento do bloco [158, 273, 426].

### 3.2. Padronização de Cadastro e Conversão de Unidades
* **Registro 0200 (Produtos/Serviços)**: Define o código interno do item e sua Nomenclatura Comum do Mercosul (NCM) [26, 142]. O cadastro deve ser rigorosamente mantido com os códigos de NCM oficiais e vigentes na tabela da Receita Federal [26, 30].
* **Unidades de Medida (0190 e 0220)**: Toda unidade de medida utilizada nas entradas, saídas ou inventário precisa estar previamente cadastrada no Registro `0190` [141, 142, 174].
* **Fator de Conversão (`0220`)**: Quando a unidade de comercialização informada na NF-e (ex.: caixa, duzia) for diferente da unidade de controle de estoque cadastrada no Registro `0200` (ex.: unidade, kg), é obrigatório informar o fator de conversão de unidades no Registro `0220` [174, 184].

---

## 4. Assinatura Digital e Requisitos ICP-Brasil

Todo arquivo digital da EFD ICMS/IPI deve ser assinado eletronicamente para garantir validade jurídica, autenticidade, integridade e não repúdio [41, 42, 96, 110].

* **Padrão do Certificado**: Certificados digitais do tipo **A1** (arquivo digital em software) ou **A3** (cartão inteligente ou token criptográfico) emitidos por Autoridade Certificadora credenciada na ICP-Brasil [40, 96, 102].
* **Titularidade da Assinatura**:
  1. Certificado e-PJ ou e-CNPJ contendo o mesmo CNPJ base (8 primeiros dígitos) da matriz ou filial declarante [106].
  2. Certificado e-PF ou e-CPF do representante legal da empresa cadastrado na Receita Federal do Brasil [106].
  3. Procurador legalmente constituído via procuração eletrônica cadastrada no portal e-CAC da Receita Federal ou SEFAZ [107].

---

## 5. Fluxo de Validação e Transmissão via PVA

A validação, assinatura e transmissão da EFD ICMS/IPI são realizadas por meio do **Programa Validador e Assinador (PVA)** oficial disponibilizado pelo SPED [2, 3, 102].

```
+-------------------------------------------------------+
|  1. Exportação do Arquivo TXT pelo ERP/Sistema       |
+-------------------------------------------------------+
                           |
                           v
+-------------------------------------------------------+
|  2. Importação no PVA (Requer Java JVM 1.5+)          |
+-------------------------------------------------------+
                           |
                           v
+-------------------------------------------------------+
|  3. Execução das Regras de Validação Estrutural       |
+-------------------------------------------------------+
              /                           \
             /                             \
            v                               v
+-----------------------+       +-----------------------+
| Relatório de ERROS    |       | Relatório de ADVERT.  |
| (Impede assinatura)   |       | (Permite o envio)     |
+-----------------------+       +-----------------------+
            |                               |
            v                               v
+-----------------------+       +-----------------------+
| Correção no ERP/PVA   |       | Análise fiscal prévia |
+-----------------------+       +-----------------------+
            \                             /
             \                           /
              v                         v
+-------------------------------------------------------+
|  4. Assinatura Digital com Certificado A1/A3 (ICP)    |
+-------------------------------------------------------+
                           |
                           v
+-------------------------------------------------------+
|  5. Transmissão via Receitanet e Obtenção do Recibo   |
+-------------------------------------------------------+
```

### 5.1. Distinção entre Erros e Advertências no PVA
* **Erros (Impeditivos)**: Representam inconsistências estruturais, quebras de chaves primárias, duplicidades ou falhas graves na apuração de valores [3.2, 8]. **Bloqueiam a assinatura e a transmissão** até que sejam corrigidos no sistema gerador ou diretamente no PVA [3.2, 8].
* **Advertências (Informativas)**: Indicam pontos de atenção técnica ou fiscal (ex.: diferenças de alíquota esperada) [3.2, 8, 160]. **Não impedem o envio**, mas devem ser auditadas para evitar penalidades em fiscalizações futuras [3.2, 8].

---

## 6. Principais Erros na Validação e Ações Preventivas

| Código / Registro | Descrição da Inconsistência | Causa Raiz | Ação Corretiva / Prevenção |
| :--- | :--- | :--- | :--- |
| **Registro 0200** | NCM inválido, desatualizado ou inexistente na tabela oficial [26, 64]. | Falta de atualização da tabela NCM no ERP [26]. | Sincronizar o ERP com as tabelas vigentes disponibilizadas pela Receita Federal [30, 65]. |
| **Registro C100 / C170 / C190** | Divergência entre o Valor Total da NF-e (`C100`) e a soma dos Itens (`C170`) ou do Resumo Analítico (`C190`) [27, 28, 58]. | Digitação manual fora do ERP, parâmetros imprecisos de frete/desconto/arredondamento [58, 59]. | Configurar o ERP para efetuar o cálculo automático dos totais e bloquear edições manuais em notas autorizadas [59]. |
| **Sequência de NF-e** | Quebra na sequência numérica de documentos fiscais sem nota correspondente [61]. | Extravio de XMLs, omissão de notas canceladas ou falta de lançamento de inutilizações [61, 62]. | Conferir a sequência do faturamento e registrar todas as notas canceladas e inutilizadas no arquivo [62]. |
| **Registro 0220** | Unidade comercializada na NF-e divergente da unidade de estoque do item sem fator de conversão [63, 174]. | Ausência do cadastro da relação de conversão entre unidades no ERP [174, 184]. | Cadastrar a regra de conversão de unidades no Registro `0220` para cada item com unidades distintas [174, 184]. |
| **Estrutura de Blocos** | Omissão de registros de abertura ou fechamento de blocos sem movimentação (ex.: Bloco K ou H) [67, 68]. | Desconfiguração no módulo gerador do SPED no software [68, 69]. | Garantir que o módulo fiscal exporte obrigatoriamente a abertura e encerramento de todos os blocos, inclusive vazios [69]. |

---

## 7. Diretrizes para a Transição da Reforma Tributária (Guia 3.2.2 a 3.2.4)

Nas versões consolidadas do Guia Prático da EFD ICMS/IPI (versões 3.2.2, 3.2.3 e 3.2.4), foram estabelecidas orientações oficiais sobre a convivência com os novos impostos sobre o consumo (CBS, IBS e Imposto Seletivo - IS) [37, 119, 120]:

1. **Não Apuração dos Novos Tributos na EFD**: A EFD ICMS/IPI **não é utilizada para a apuração** dos tributos da Reforma Tributária (CBS, IBS e IS) [37, 119].
2. **Valor Total do Documento Fiscal (`VL_DOC`)**: Os valores de CBS, IBS e IS que constarem nos documentos fiscais mistos devem ser integrados ao **Valor Total do Documento Fiscal** (ex.: Campo 12 `VL_DOC` do Registro `C100`, ressalvadas as regras operacionais do exercício de 2026) [119, 120].
3. **Exclusão nos Registros Analíticos (`VL_OPR`)**: Os valores de CBS, IBS e IS **não devem ser incluídos** no valor da operação dos registros analíticos (ex.: Campo 05 `VL_OPR` do Registro `C190`) [119, 120].
