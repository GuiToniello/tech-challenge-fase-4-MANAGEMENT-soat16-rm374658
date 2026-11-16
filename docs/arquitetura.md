# Arquitetura da solução

Este documento dá a visão geral de como o sistema da oficina funciona de ponta a ponta. Ele junta as peças dos três repositórios do projeto:
- **este monorepo**: código da Management API em `server/`; rede, Kubernetes, API Gateway e Datadog em [kubernetes/](../kubernetes/README.md); banco de dados em [DB/](../DB/README.md); filas em [SQS/](../SQS/README.md);
- o [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658): o microsserviço de ordens de serviço, com banco próprio;
- o [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658): autenticação.

Na fase 4, os repositórios APP, DB e K8S da fase 3 viraram pastas deste monorepo, com uma só API, a Management API ([ADR-018](./adr/ADR-018-Monorepo-e-API-Unica-Management.md)). Depois, o ciclo de vida da ordem de serviço saiu da Management API e virou o OS Service ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)). Os dois serviços conversam só por eventos no Amazon SQS ([ADR-020](./adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)).

Neste documento:
1. Glossário rápido.
2. Diagrama de componentes: a nuvem, as APIs, os bancos, as filas e o monitoramento.
3. Diagrama de sequência da autenticação.
4. Diagramas de sequência da abertura de uma ordem de serviço e do orçamento (a saga entre os serviços).
5. Índice das decisões de arquitetura (RFCs e ADRs) dos três repositórios.

Os diagramas usam [Mermaid](https://mermaid.js.org/), que o GitHub desenha sozinho. Eles são uma **visão geral**: escondem detalhes como a rede interna e as pipelines, que estão nos documentos indicados ao longo do texto.

## 1. Glossário rápido

| Termo | O que significa aqui |
|---|---|
| API | Programa que recebe pedidos pela internet (requisições HTTP) e responde em JSON. O sistema tem duas APIs em .NET: a Management API e o OS Service |
| Management API | Cadastros (clientes, veículos, serviços e peças), estoque, orçamento e e-mails |
| OS Service | Microsserviço das ordens de serviço: abertura, status, consultas e indicadores |
| OS | Ordem de serviço: o pedido de conserto de um veículo, com os serviços e as peças (insumos) |
| Evento | Mensagem que um serviço publica para avisar o outro de algo que já aconteceu, como "OS aberta" ou "orçamento aprovado" |
| Fila (Amazon SQS) | Onde os eventos esperam até o outro serviço lê-los. A fila FIFO entrega os eventos de uma mesma OS na ordem em que foram publicados |
| DLQ | Fila de mensagens com erro (*dead-letter queue*): recebe o evento que falhou 5 vezes, para análise |
| Saga | Sequência de passos em serviços diferentes, sem uma transação única. Se um passo falha, um evento de compensação desfaz o anterior |
| JWT (token) | Um "crachá digital" assinado, que prova quem é o usuário. Vai em cada requisição, no header `Authorization` |
| Claim | Uma informação gravada dentro do JWT. Aqui, a mais importante é o `cpf` do cliente |
| AWS | A nuvem da Amazon, onde o sistema roda, na região `us-east-1` (leste dos EUA) |
| API Gateway | A porta de entrada pública. Recebe todas as requisições e envia cada uma para o lugar certo |
| Lambda | Função que roda na AWS só quando é chamada, sem servidor para administrar |
| VPC | A rede do projeto na AWS. Os bancos ficam numa parte dela sem acesso à internet |
| Kubernetes (EKS) | Sistema que roda as APIs em containers e as reinicia se caírem. O Amazon EKS é o Kubernetes gerenciado pela AWS |
| Pod | Uma cópia de uma API rodando no Kubernetes |
| HPA | Regra do Kubernetes que cria ou remove pods de uma API conforme o uso de CPU e memória |
| RDS (PostgreSQL) | Banco de dados relacional (organizado em tabelas), gerenciado pela AWS. Cada API tem o seu |
| Auth0 | Serviço externo que guarda os usuários (e-mail e senha) e emite os JWTs |
| Datadog | Serviço externo de monitoramento: recebe logs, métricas e traces, mostra painéis e envia alertas |
| Resend | Serviço externo de envio de e-mail |

## 2. Diagrama de componentes

O diagrama mostra as peças principais do sistema e quem conversa com quem.

```mermaid
flowchart LR
  Usuario([Usuario<br/>Postman])

  subgraph AWS[AWS - regiao us-east-1]
    GW[API Gateway<br/>entrada unica, HTTPS]
    Lambdas[Lambdas de autenticacao<br/>pedem o JWT ao Auth0<br/>e conferem o JWT]

    subgraph SQS[Amazon SQS - filas FIFO]
      FilaM[[Fila da Management]]
      FilaOS[[Fila do OS Service]]
    end

    subgraph VPC[VPC - rede do projeto]
      subgraph EKS[Kubernetes - Amazon EKS]
        API[Management API .NET<br/>management-api<br/>HPA: 1 a 5 pods]
        OS[OS Service .NET<br/>os-service-api<br/>HPA: 1 a 5 pods]
      end
      DB[(PostgreSQL<br/>Amazon RDS<br/>oficina)]
      DBOS[(PostgreSQL<br/>Amazon RDS<br/>oficina_os)]
    end
  end

  subgraph Externos[Servicos externos]
    Auth0[Auth0<br/>usuarios e JWT]
    Resend[Resend<br/>e-mail]
    Datadog[Datadog<br/>monitoramento]
  end

  Usuario -->|HTTPS| GW
  GW -->|login e autorizacao| Lambdas
  GW -->|/management| API
  GW -->|/os| OS
  Lambdas -->|consulta o CPF| DB
  Lambdas -->|pede o JWT| Auth0
  API -->|le e grava| DB
  OS -->|le e grava| DBOS
  OS -->|publica eventos| FilaM
  FilaM -->|consome| API
  API -->|publica eventos| FilaOS
  FilaOS -->|consome| OS
  API -->|orcamento e status| Resend
  API -.->|logs, metricas e traces| Datadog
  OS -.->|logs, metricas e traces| Datadog
```

Como ler: a seta cheia é uma chamada ou uma mensagem; a tracejada é o envio dos dados de monitoramento. As setas das filas são assíncronas: quem publica não espera o outro serviço processar o evento.

### 2.1. As peças do diagrama

| Peça | Papel | Onde fica |
|---|---|---|
| API Gateway | Única entrada pública. As rotas com o prefixo `/management` vão para a Management API, as com `/os` vão para o OS Service, e `POST /auth/token` vai para a Lambda de token | `kubernetes/` |
| Lambdas de autenticação | A Lambda de token confere o CPF, pede ao Auth0 o JWT com o CPF do cliente e devolve esse JWT (seção 3). O Lambda authorizer confere o JWT antes de cada requisição chegar às APIs | Repo LAMBDA |
| Management API (.NET) no EKS | Cadastros (clientes, veículos, serviços e insumos com estoque), orçamento (gerar, enviar, aprovar e recusar) e e-mails. Guarda uma referência de cada OS, recebida por eventos, para validar a OS e orçar. O HPA ajusta de 1 a 5 pods | `server/` (código) e `kubernetes/` (cluster) |
| OS Service (.NET) no EKS | Ciclo de vida da OS: abertura, atualização, lista por prioridade, acompanhamento, mudanças de status e indicadores. A OS guarda só os ids do cliente, do veículo e dos serviços. O HPA ajusta de 1 a 5 pods | Repo OS-SERVICE (código, banco e manifests). A rota `/os` e o NLB ficam em `kubernetes/` deste monorepo |
| Filas Amazon SQS FIFO | Uma fila de entrada por serviço, cada uma com a sua DLQ. Levam os eventos da saga entre as duas APIs | `SQS/` |
| PostgreSQL da Management (Amazon RDS) | Banco da Management API, também lido pela Lambda `buscar-cliente` (repo LAMBDA). Sem acesso pela internet | `DB/` |
| PostgreSQL do OS Service (Amazon RDS) | Banco do OS Service (`oficina_os`), na mesma rede. Só o OS Service o acessa | Repo OS-SERVICE |
| Auth0 | Guarda os usuários e emite e assina os JWTs | Externo (configurado no repo LAMBDA) |
| Resend | Envia o orçamento e, se ligado, o e-mail de mudança de status | Externo |
| Datadog | Monitoramento das duas APIs: logs, métricas, traces, alertas e dashboard | Externo (configurado em `kubernetes/`) |

Detalhes que o diagrama esconde:
- **Rede:** entre o API Gateway e as APIs há um VPC Link e um balanceador interno (NLB), e os bancos ficam numa sub-rede sem internet. Os pods chegam ao SQS pela saída das sub-redes públicas, como chegam ao ECR. Veja o [kubernetes/infra/ESTRUTURA.md](../kubernetes/infra/ESTRUTURA.md).
- **Kubernetes:** Deployment, Service, ConfigMap, Secret e HPA da Management API estão no [kubernetes/k8s/README.md](../kubernetes/k8s/README.md); os do OS Service, no repo OS-SERVICE (namespace `os-service`).
- **Filas e credenciais:** cada serviço acessa o SQS com um usuário IAM próprio, cuja chave fica no SSM Parameter Store e vai para o Secret dos pods. Veja o [SQS/README.md](../SQS/README.md).
- **Monitoramento:** o Datadog Agent roda dentro do cluster e envia os dados ao Datadog. Veja [observabilidade.md](./observabilidade.md) e o [kubernetes/infra/DATADOG.md](../kubernetes/infra/DATADOG.md).
- **Entrega de versões (CI/CD):** cada repositório tem as próprias pipelines. Para subir: Bootstrap deste monorepo → Bootstrap do OS-SERVICE → Deploy do LAMBDA. Para derrubar: Destroy do LAMBDA e do OS-SERVICE → Destroy deste monorepo. Veja as pipelines [deste monorepo](../.github/workflows/README.md), do [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/.github/workflows/README.md) e do [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658#9-pipelines-github-actions).

## 3. Diagrama de sequência: autenticação

Um diagrama de sequência mostra, de cima para baixo, a ordem das mensagens trocadas entre as peças. Este mostra como um cliente **já cadastrado** consegue o JWT com o seu CPF, exigido em todas as rotas das APIs.

```mermaid
sequenceDiagram
  autonumber
  actor Usuario
  participant GW as API Gateway
  participant Lambda as Lambda de token
  participant DB as PostgreSQL
  participant Auth0

  Usuario->>GW: POST /auth/token com email, senha e cpf
  GW->>Lambda: Encaminha o pedido
  Lambda->>DB: Este CPF tem cadastro?
  DB-->>Lambda: Sim
  Lambda->>Auth0: Pede o token com email, senha e cpf
  Auth0-->>Lambda: JWT assinado, com a claim cpf
  Lambda-->>GW: 200 com o JWT
  GW-->>Usuario: 200 com o JWT
  Note over Usuario,Auth0: CPF sem cadastro = 404. Email ou senha errados = 401.
```

Pontos importantes:
- **Quem cria e assina o token é o Auth0.** A Lambda confere se o CPF tem cadastro, pede o token ao Auth0 e devolve esse token ao usuário.
- **A "Lambda de token" do diagrama é formada por duas funções:** uma consulta o banco da Management, dentro da rede do projeto, e a outra fala com o Auth0, fora dela. Assim o banco continua sem acesso à internet.
- **Cliente novo:** quem ainda não tem cadastro pega um token direto no Auth0 (sem `cpf`), se cadastra em `POST /management/api/clientes`, a única rota que aceita esse token, e depois segue o fluxo acima.
- **O mesmo JWT vale para as duas APIs:** o Lambda authorizer protege as rotas `/management/...` e `/os/...`, e cada API confere o JWT de novo.
- Respostas de erro e configuração do Auth0: [README do repo LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658#2-fluxos). Opções avaliadas: [RFC-003](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md).

## 4. Diagrama de sequência: abertura de ordem de serviço

A OS é aberta no OS Service, que guarda só os ids do cliente, do veículo e dos serviços. Quem confere se esses cadastros existem é a Management, ao receber o evento da OS. É uma saga coreografada: cada serviço faz a sua parte e avisa o outro por evento ([ADR-020](./adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)). O usuário já tem o JWT da seção 3.

### 4.1. Abertura e validação da OS

```mermaid
sequenceDiagram
  autonumber
  actor Usuario
  participant GW as API Gateway
  participant OS as OS Service
  participant FilaM as Fila da Management
  participant API as Management API
  participant FilaOS as Fila do OS Service

  Usuario->>GW: POST /os/api/ordens-servico<br/>com o JWT, clienteId, veiculoId e servicoIds
  Note over GW: O Lambda authorizer confere o JWT e o cpf
  GW->>OS: Encaminha o pedido
  OS->>OS: Grava a OS com status Recebida
  OS->>FilaM: Publica OrdemServicoAberta
  OS-->>GW: 201 com o ID da OS
  GW-->>Usuario: 201 com o ID da OS
  FilaM->>API: Entrega OrdemServicoAberta
  API->>API: Cliente, veiculo do cliente e servicos existem?
  alt Dados validos
    API->>API: Grava a referencia da OS
  else Algum cadastro nao existe
    API->>FilaOS: Publica OrdemServicoRejeitada com o motivo
    FilaOS->>OS: Entrega OrdemServicoRejeitada
    OS->>OS: Cancela a OS e guarda o motivo (compensacao)
  end
  Note over Usuario,FilaOS: Sem token = 401. Token invalido ou sem cpf = 403. Dados invalidos na abertura (sem servicos, por exemplo) = 400.
```

### 4.2. Orçamento

As requisições passam pelo API Gateway e pelo Lambda authorizer, omitidos aqui.

```mermaid
sequenceDiagram
  autonumber
  actor Usuario
  participant API as Management API
  participant FilaOS as Fila do OS Service
  participant OS as OS Service
  participant FilaM as Fila da Management

  Usuario->>API: POST /management/api/orcamentos/{id}/gerar
  API->>API: Confere o estoque e grava o orcamento
  API->>FilaOS: Publica OrcamentoGerado com o valor
  API-->>Usuario: 200 com o orcamento
  FilaOS->>OS: Entrega OrcamentoGerado
  OS->>OS: OS em AguardandoAprovacao, com o valor
  OS->>FilaM: Publica OrdemServicoStatusAlterado
  Usuario->>API: POST /management/api/orcamentos/{id}/aprovar
  API->>API: Confere e debita o estoque, orcamento Aprovado
  API->>FilaOS: Publica OrcamentoAprovado
  API-->>Usuario: 200 com o orcamento
  FilaOS->>OS: Entrega OrcamentoAprovado
  OS->>OS: OS em EmExecucao
  OS->>FilaM: Publica OrdemServicoStatusAlterado
  FilaM->>API: Entrega OrdemServicoStatusAlterado
  API->>API: Atualiza o status da referencia e, se ligado, envia o e-mail de status
  Note over Usuario,FilaM: Recusar (POST .../recusar) publica OrcamentoRecusado, e o OS Service leva a OS para Finalizada.
```

Pontos importantes:
- **A resposta não espera o outro serviço.** O 201 da abertura sai antes da validação na Management. Se algum cadastro não existir, a OS passa para `Cancelada` alguns segundos depois, e o acompanhamento (`GET /os/api/ordens-servico/{id}/acompanhamento`) mostra o `motivoCancelamento`.
- **Consistência eventual.** Logo depois da abertura, a Management pode ainda não conhecer a OS: gerar o orçamento responde 404 até o evento chegar. Basta esperar alguns segundos; a collection do Postman do repo OS-SERVICE espera entre os passos.
- **Compensação.** O cancelamento também publica `OrdemServicoStatusAlterado`, que a Management ignora, porque não tem a referência de uma OS rejeitada.
- **Atualização e exclusão.** O `PUT` (só com a OS `Recebida`) publica `OrdemServicoAtualizada`, validada da mesma forma. A exclusão publica `OrdemServicoExcluida`, e a Management apaga a referência e o orçamento.
- **Mudanças de status pelo OS Service** (`/em-diagnostico`, `/em-execucao`, `/finalizar` e `/entregar`) também publicam `OrdemServicoStatusAlterado`. A Management avisa o cliente por e-mail nos status `EmDiagnostico`, `EmExecucao`, `Finalizada` e `Entregue`, se o e-mail de status estiver ligado (vem desligado).
- **Ordem e falhas.** A fila FIFO entrega os eventos de uma OS na ordem; um evento que falha volta para a fila e, depois de 5 tentativas, vai para a DLQ ([ADR-020](./adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)).
- Os logs gerados (`os.criada`, `os.status_alterado`, `evento.publicado`, `evento.consumido` etc.) estão em [observabilidade.md](./observabilidade.md). O padrão de comunicação dos clientes com as APIs (HTTP síncrono pelo API Gateway) está na [ADR-015](./adr/ADR-015-Comunicacao-Sincrona-via-API-Gateway.md).

## 5. Decisões de arquitetura (RFCs e ADRs)

As decisões ficam registradas em dois tipos de documento:
- **RFC (Request for Comments):** proposta escrita que apresenta um problema, compara as opções e recomenda uma. Serve para discutir antes de decidir.
- **ADR (Architecture Decision Record):** registro de uma decisão tomada, com o contexto, as consequências e as alternativas rejeitadas. Explica "por que é assim".

As RFCs e ADRs deste monorepo ficam em `docs/rfc/` e `docs/adr/`. A ADR-019, do OS Service, fica no repo OS-SERVICE, e a RFC-003, de autenticação, no repo LAMBDA. A numeração é compartilhada entre os três repositórios.

### 5.1. RFCs

| RFC | Assunto | Resultado | Repositório |
|---|---|---|---|
| [RFC-001](./rfc/RFC-001-Escolha-da-Nuvem.md) | Escolha da nuvem | AWS, região `us-east-1` | Este monorepo |
| [RFC-002](./rfc/RFC-002-Escolha-do-Banco-de-Dados.md) | Escolha do banco de dados | PostgreSQL no Amazon RDS, registrado na ADR-017 | Este monorepo |
| [RFC-003](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md) | Estratégia de autenticação | O Auth0 continua emitindo o JWT, e o API Gateway exige a claim `cpf` com um Lambda authorizer | LAMBDA |
| [RFC-004](./rfc/RFC-004-Comunicacao-entre-Microsservicos.md) | Comunicação entre microsserviços | Amazon SQS FIFO, uma fila de entrada por serviço, com saga coreografada, registrado na ADR-020 | Este monorepo |

### 5.2. ADRs

| ADR | Assunto | Repositório |
|---|---|---|
| ADR-001 a ADR-014 | Organização do código, persistência, e-mail, estoque, tratamento de erros e observabilidade. Índice em [docs/adr/INDICE.md](./adr/INDICE.md). A ADR-011, a ADR-012 e a ADR-014 foram atualizadas na fase 4 | Este monorepo |
| [ADR-015](./adr/ADR-015-Comunicacao-Sincrona-via-API-Gateway.md) | Padrão de comunicação dos clientes com as APIs: síncrono (HTTP/REST) via API Gateway, sem chamadas HTTP entre as APIs | Este monorepo |
| [ADR-016](./adr/ADR-016-Escalabilidade-com-HPA.md) | Escalabilidade das APIs com HPA, de 1 a 5 pods cada | Este monorepo |
| [ADR-017](./adr/ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md) | Banco PostgreSQL no Amazon RDS: justificativa formal, diagrama ER e relacionamentos do banco da Management | Este monorepo |
| [ADR-018](./adr/ADR-018-Monorepo-e-API-Unica-Management.md) | Monorepo (os repositórios APP, DB e K8S da fase 3 viraram pastas) e uma só API neste monorepo, a Management API | Este monorepo |
| [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md) | OS Service: o ciclo de vida da OS num microsserviço com banco, namespace e pipelines próprios | OS-SERVICE |
| [ADR-020](./adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) | Eventos em filas Amazon SQS FIFO e saga coreografada entre os serviços | Este monorepo |
