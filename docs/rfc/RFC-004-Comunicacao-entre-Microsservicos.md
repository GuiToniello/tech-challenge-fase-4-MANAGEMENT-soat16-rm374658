# RFC-004: Comunicação entre microsserviços

> Uma RFC (Request for Comments) é uma proposta escrita: apresenta um problema, compara as opções e recomenda uma, para que a decisão seja discutida antes de ser adotada. As RFCs e ADRs são numeradas em sequência única entre este monorepo e os repositórios [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice está no [docs/arquitetura.md](../arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

- **Status**: Aceita
- **Partes afetadas**: neste monorepo, `server/` (orçamento e mensageria), `SQS/` (filas) e as pipelines; fora dele, o repo OS-SERVICE, que publica e consome os mesmos eventos
- **Resultado**: Amazon SQS FIFO, com uma fila de entrada por serviço e uma saga coreografada. A decisão está registrada na [ADR-020](../adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)

## 1. Resumo

Na fase 4, o ciclo de vida da ordem de serviço (OS) saiu da Management API e virou o OS Service, um microsserviço com banco próprio ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)). Os dois serviços precisam trocar informações: o OS Service não conhece os cadastros (clientes, veículos e serviços) nem o orçamento, que ficam na Management, e a Management precisa saber das OS abertas e das mudanças de status.

A proposta é trocar **eventos assíncronos** por filas **Amazon SQS FIFO**, uma fila de entrada por serviço, numa **saga coreografada**: cada serviço reage aos eventos do outro, e uma OS inválida é desfeita por um evento de compensação.

## 2. Contexto

- Cada serviço tem o próprio banco (*database per service*), e nenhum lê o banco do outro (ADR-019).
- Os clientes (Postman) continuam chamando as duas APIs por HTTPS, pelo mesmo API Gateway (`/management/...` e `/os/...`), com o Lambda authorizer ([ADR-015](../adr/ADR-015-Comunicacao-Sincrona-via-API-Gateway.md) e RFC-003). Esta RFC trata só da conversa entre os serviços.
- Os dois serviços rodam no mesmo cluster pequeno: 2 nós `t3.small` fixos, quase cheios ([ADR-016](../adr/ADR-016-Escalabilidade-com-HPA.md)).
- É um projeto de estudo na AWS, na região `us-east-1` ([RFC-001](RFC-001-Escolha-da-Nuvem.md)): o custo precisa ficar perto de zero, o ambiente é destruído depois da demonstração, e tudo é criado por Terraform e pelas pipelines.

O que cada serviço precisa saber do outro:

| De | Para | O quê |
|---|---|---|
| OS Service | Management | OS aberta, atualizada ou excluída (para validar os cadastros e orçar) e cada mudança de status (para avisar o cliente) |
| Management | OS Service | OS rejeitada (cadastro inexistente) e orçamento gerado, aprovado ou recusado (para mudar o status da OS) |

## 3. Requisitos

| # | Requisito | Exemplo no sistema |
|---|---|---|
| M1 | Um serviço fora do ar não pode derrubar o outro | Abrir uma OS enquanto a Management reinicia |
| M2 | Ordem garantida entre os eventos de uma mesma OS | `OrcamentoGerado` processado antes de `OrcamentoAprovado` |
| M3 | Nova tentativa automática e um lugar para as mensagens que sempre falham | Banco indisponível durante o processamento |
| M4 | Desfazer um passo quando o seguinte falha (compensação) | OS aberta com um cliente inexistente é cancelada |
| M5 | Serviço gerenciado, criado por Terraform, sem pods novos no cluster | O cluster já está no limite |
| M6 | Custo perto de zero | Projeto de estudo |
| M7 | Fácil de rodar e de testar localmente | `docker compose` e testes sem AWS |

## 4. Opções avaliadas

### 4.1. Transporte

| Opção | Prós | Contras |
|---|---|---|
| HTTP síncrono entre as APIs | Nenhuma peça nova; resposta imediata | Não atende M1: se a Management cair, abrir uma OS falha. Exige timeout, nova tentativa e autenticação em cada chamada interna, e a compensação fica toda no código de quem chama |
| Amazon SQS Standard | Gerenciado, com DLQ nativa e dentro do nível gratuito (1 milhão de requisições por mês) | Não garante ordem (M2) e pode entregar a mesma mensagem mais de uma vez: o consumidor teria de reordenar os eventos |
| **Amazon SQS FIFO** | Ordem por grupo (`MessageGroupId` = id da OS), deduplicação pelo id da mensagem, DLQ nativa, o mesmo nível gratuito, SDK oficial para .NET e um emulador local leve (ElasticMQ) | Ponto a ponto: cada fila tem um consumidor. Para vários interessados no mesmo evento, precisa de um tópico na frente. A vazão por fila é limitada, mas muito acima do que o projeto usa |
| Amazon SNS + SQS (*fan-out*) | Um evento publicado uma vez chega a vários serviços | Com só dois serviços, soma tópicos, assinaturas e políticas sem ganho. Para ter ordem, exige SNS FIFO com SQS FIFO |
| Amazon EventBridge | Barramento com regras de roteamento por conteúdo | Não garante ordem (M2). Para ter nova tentativa e DLQ por consumidor, o alvo acaba sendo uma fila SQS, com mais configuração (barramento, regras e alvos) |
| Apache Kafka (Amazon MSK) | Log de eventos com releitura (*replay*), ordem por partição e alto volume | Cobrado por hora de broker, mesmo sem uso (M6), e muito maior do que o projeto precisa. Kafka dentro do cluster ocuparia pods e memória que não existem (M5) |
| RabbitMQ (Amazon MQ) | Roteamento flexível (*exchanges*) | O Amazon MQ é cobrado por hora de broker (M6). RabbitMQ dentro do cluster ocuparia pods e memória (M5) e seria mais uma peça para operar |

### 4.2. Orquestração ou coreografia

| Opção | Prós | Contras |
|---|---|---|
| Orquestração: um coordenador (o próprio OS Service ou o AWS Step Functions) manda cada serviço executar um passo | O fluxo inteiro fica num lugar só | O coordenador precisa conhecer os passos do outro serviço (acoplamento). Com o Step Functions, seria mais um serviço da AWS para criar e pagar; dentro do OS Service, uma máquina de estados a mais |
| **Coreografia**: cada serviço publica o que aconteceu e reage ao que o outro publicou | Sem peça central: cada serviço só conhece os eventos. Com dois serviços e poucos passos, o fluxo é curto | O fluxo fica espalhado pelos dois serviços: precisa de um diagrama ([arquitetura.md](../arquitetura.md#4-diagrama-de-sequência-abertura-de-ordem-de-serviço), seção 4) e de logs ligados pelo id da OS |

**Comparação:** só as filas FIFO atendem M1 e M2 juntas sem peças a mais. Entre as opções com fila FIFO, o SQS FIFO é o único gerenciado, dentro do nível gratuito e sem ocupar o cluster (M5 e M6). Com dois serviços, a coreografia é mais simples do que um orquestrador.

## 5. Proposta

- **Uma fila FIFO de entrada por serviço:** `techchallenge-oficina-management.fifo`, consumida pela Management, e `techchallenge-oficina-os-service.fifo`, consumida pelo OS Service. Cada serviço publica na fila do outro.
- **Uma DLQ por fila:** depois de 5 recebimentos sem sucesso, a mensagem vai para a `...-dlq.fifo`, que a guarda por 14 dias.
- **Ordem por OS:** `MessageGroupId` = id da OS, e `MessageDeduplicationId` = id do evento.
- **Saga coreografada, com compensação:** a Management valida a OS aberta e, se algum cadastro não existir, publica `OrdemServicoRejeitada`; o OS Service então cancela a OS. O orçamento (gerar, aprovar e recusar) muda o status da OS pelos eventos `OrcamentoGerado`, `OrcamentoAprovado` e `OrcamentoRecusado`.
- **Infraestrutura em Terraform**, na pasta [SQS/](../../SQS/README.md) deste monorepo, com um usuário IAM por serviço e a chave dele no SSM Parameter Store.
- **Localmente**, o ElasticMQ faz o papel do SQS no `docker compose`.

O envelope, a lista de eventos, a idempotência e as credenciais estão na ADR-020.

## 6. Riscos e como reduzi-los

| Risco | Como reduzir |
|---|---|
| Sem transação entre o banco e a fila: o serviço grava e depois publica. Se a publicação falhar, a alteração fica gravada sem o evento | A falha gera o log `integracao.falha` (`sqs`, `publicacao`), e a requisição responde 500. Evolução: *transactional outbox* |
| A fila entrega pelo menos uma vez: o mesmo evento pode chegar de novo depois de uma falha | Consumidores idempotentes: evento repetido, transição fora de ordem e OS desconhecida são ignorados (`evento.ignorado`) |
| Uma mensagem com erro segura as próximas da mesma OS (FIFO) | Depois de 5 recebimentos (cerca de 5 min, com o *visibility timeout* de 60 s), ela vai para a DLQ e libera as próximas. A DLQ é analisada e reprocessada manualmente |
| Consistência eventual: logo depois de abrir a OS, a Management ainda pode não conhecê-la | Gerar o orçamento responde 404 até o evento chegar; basta tentar de novo em alguns segundos (a collection do Postman espera entre os passos) |
| Um novo serviço interessado nos mesmos eventos (por exemplo, faturamento ou execução) | Evolução: um tópico SNS FIFO na frente das filas (*fan-out*), sem mudar os consumidores atuais |

## 7. Resultado

Aceita e implementada. As filas estão em [SQS/](../../SQS/README.md), e a camada de mensageria fica em `server/` (projeto `TechChallenge.Oficina.Messaging`) e no repo OS-SERVICE. A decisão, com o envelope, os eventos, a saga e as credenciais, está na [ADR-020](../adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md).
