# ADR-020: Eventos com Amazon SQS e Saga Coreografada

**Status**: Aceita (fase 4). Supera o item 7 da [ADR-015](ADR-015-Comunicacao-Sincrona-via-API-Gateway.md) (sem mensageria) e a alternativa de fila deixada como evolução futura na [ADR-011](ADR-011-Envio-de-Email-de-Orcamento.md)

**Resumo**:
A Management API e o OS Service ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)) conversam só por eventos em filas Amazon SQS FIFO, uma fila de entrada por serviço. Não há chamadas HTTP entre as APIs. O fluxo da OS é uma saga coreografada: cada serviço grava a própria parte, publica um evento e reage aos eventos do outro. Se a Management não aceita a OS, o OS Service a cancela (compensação). As opções avaliadas estão na [RFC-004](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md).

> As ADRs são numeradas em sequência única entre este monorepo e os repositórios [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice de todas as decisões está no [docs/arquitetura.md](../arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

**Contexto**:
- Com o OS Service, cada serviço tem o próprio banco, e nenhum lê o banco do outro (ADR-019). A OS guarda só os ids do cliente, do veículo e dos serviços, e o orçamento fica na Management.
- O OS Service precisa saber se a OS é válida, porque os cadastros são da Management, e quando o orçamento muda. A Management precisa conhecer as OS, para orçar e para avisar o cliente.
- Até a fase 3 não havia comunicação entre serviços nem mensageria (ADR-015, item 7).
- O cluster é pequeno (ADR-016), o custo precisa ficar perto de zero, e o ambiente é recriado com frequência: não pode haver passo manual nem secret novo a cada recriação.

**Decisão**:

1. **Duas filas FIFO, uma de entrada por serviço**, criadas pelo Terraform em [SQS/infra](../../SQS/README.md):

   | Fila | Quem consome | Quem publica | DLQ |
   |---|---|---|---|
   | `techchallenge-oficina-management.fifo` | Management | OS Service | `techchallenge-oficina-management-dlq.fifo` |
   | `techchallenge-oficina-os-service.fifo` | OS Service | Management | `techchallenge-oficina-os-service-dlq.fifo` |

   - *Visibility timeout* de 60 s. Depois de 5 recebimentos sem sucesso (`maxReceiveCount`), a mensagem vai para a DLQ, que a guarda por 14 dias.
   - `MessageGroupId` = id da OS, o que garante a ordem entre os eventos de uma mesma OS, e `MessageDeduplicationId` = id do envelope. Não há deduplicação por conteúdo.

2. **Envelope JSON (camelCase)**, o mesmo nos dois sentidos:

   ```json
   {"id":"<guid>","tipo":"OrcamentoGerado","versao":1,"ocorridoEm":"2026-11-14T22:00:00Z","origem":"management","dados":{"ordemServicoId":"<guid>","valorTotal":350.00}}
   ```

   - `origem` é `management` ou `os-service`. O `tipo` também vai no atributo de mensagem `tipo` do SQS.
   - Cada evento é um `record` C# na pasta `Features/Eventos/Contratos` do projeto UseCases. Cada repositório tem a própria cópia dos contratos, sem biblioteca compartilhada.

3. **Eventos**:

   | Evento | De → para | Dados | O que quem consome faz |
   |---|---|---|---|
   | `OrdemServicoAberta` | OS Service → Management | `ordemServicoId`, `clienteId`, `veiculoId`, `servicoIds` | Valida os cadastros: grava a referência da OS ou publica `OrdemServicoRejeitada` |
   | `OrdemServicoAtualizada` | OS Service → Management | Os mesmos de `OrdemServicoAberta` | Faz a mesma validação, com os dados novos |
   | `OrdemServicoStatusAlterado` | OS Service → Management | `ordemServicoId`, `clienteId`, `statusAnterior`, `statusNovo` | Atualiza o status da referência e envia o e-mail de status (ADR-011) |
   | `OrdemServicoExcluida` | OS Service → Management | `ordemServicoId` | Apaga a referência e o orçamento |
   | `OrdemServicoRejeitada` | Management → OS Service | `ordemServicoId`, `motivo` | Cancela a OS (`Cancelada`) e guarda o motivo: é a compensação |
   | `OrcamentoGerado` | Management → OS Service | `ordemServicoId`, `valorTotal` | Leva a OS para `AguardandoAprovacao`, com o valor. Um orçamento gerado de novo só atualiza o valor |
   | `OrcamentoAprovado` | Management → OS Service | `ordemServicoId` | Leva a OS para `EmExecucao` |
   | `OrcamentoRecusado` | Management → OS Service | `ordemServicoId` | Leva a OS para `Finalizada` |

   Os status vão como números do enum `StatusOrdemServico`, iguais nos dois serviços: `Recebida` 1, `EmDiagnostico` 2, `AguardandoAprovacao` 3, `EmExecucao` 4, `Finalizada` 5, `Entregue` 6, `Encerrada` 7 (sem uso) e `Cancelada` 8.

4. **Saga coreografada, com compensação** (diagramas na seção 4 da [arquitetura.md](../arquitetura.md#4-diagrama-de-sequência-abertura-de-ordem-de-serviço)):
   - O OS Service grava a OS como `Recebida` e publica `OrdemServicoAberta`. A Management confere se o cliente existe, se o veículo existe e é desse cliente e se os serviços existem.
     - **Válida:** a Management grava uma referência mínima da OS (`OrdemServicoReferencia`, com os ids, os serviços e o status), usada para orçar e para o e-mail.
     - **Inválida:** a Management publica `OrdemServicoRejeitada` com o motivo, e o OS Service cancela a OS.
   - **Orçamento**, na Management: `POST /api/orcamentos/{ordemServicoId}/gerar` → `OrcamentoGerado` → OS em `AguardandoAprovacao`; `aprovar`, com o débito de estoque da ADR-012 → `OrcamentoAprovado` → OS em `EmExecucao`; `recusar` → `OrcamentoRecusado` → OS em `Finalizada`.
   - Toda mudança de status no OS Service publica `OrdemServicoStatusAlterado`. A Management atualiza a referência e, nos status `EmDiagnostico`, `EmExecucao`, `Finalizada` e `Entregue`, envia o e-mail de status, que vem desligado (`SendEmailOnStatusChange=false`, ADR-011).

5. **Camada de mensageria, no mesmo padrão do e-mail (ADR-011)**:
   - A porta de saída `IEventosPublisher` fica no projeto UseCases (`Features/Eventos/Interfaces`).
   - A implementação fica no projeto `TechChallenge.Oficina.Messaging` (no OS Service, `TechChallenge.Oficina.OsService.Messaging`), no anel "1 - Frameworks & Drivers":
     - `SqsSettings` (`FilaConsumo`, `FilaPublicacao` e `ServiceUrl`);
     - `ISqsClient`, com o `SqsClientAdapter`, o único ponto que usa o SDK (`AWSSDK.SQS` 4.0.x);
     - `EventosPublisher`, `SqsConsumidorHostedService` e o registro `AddInfraMessaging`.
   - **Entrada pelo controller:** o consumidor lê o envelope e chama o `EventosController` (projeto Controllers) com o `tipo` e os `dados`, e o controller chama o caso de uso. É o mesmo caminho de uma requisição HTTP: endpoint → controller → caso de uso.

6. **Consumo, nova tentativa e DLQ**:
   - O consumidor é um `BackgroundService` com *long polling* de 20 s, lotes de até 10 mensagens e um escopo de injeção de dependência por mensagem, como numa requisição.
   - Evento processado ou ignorado: a mensagem é apagada.
   - Exceção: a mensagem não é apagada, volta para a fila depois do *visibility timeout* e, no 5º recebimento, vai para a DLQ. Não há reprocessamento automático da DLQ.

7. **Idempotência sem tabela extra**: evento repetido, transição que o status atual não permite e OS desconhecida são ignorados (log `evento.ignorado`), sem exceção e sem publicar nada. Exemplos: um `OrcamentoAprovado` repetido encontra a OS já `EmExecucao`; o `OrdemServicoStatusAlterado` de uma OS rejeitada não encontra a referência na Management.

8. **Publicar depois de gravar**: cada serviço grava no próprio banco e só então publica. Se a publicação falhar, sai o log `integracao.falha` (`Integracao=sqs`, `Operacao=publicacao`) e a requisição responde 500, mas a alteração gravada continua. É um risco aceito; a evolução é o *transactional outbox*.

9. **Credenciais: um usuário IAM por serviço, com a chave no SSM**:
   - O `SQS/infra` cria os usuários `techchallenge-oficina-management-sqs` e `techchallenge-oficina-os-service-sqs`, com policy inline de menor privilégio: consumir a própria fila e só publicar (`SendMessage` e `GetQueueUrl`) na fila do outro serviço.
   - A access key de cada usuário é criada pelo Terraform e guardada no SSM Parameter Store, como SecureString no tier Standard (sem custo), em `/techchallenge-oficina/sqs/<management|os-service>/access-key-id` e `/secret-access-key`.
   - O `_k8s-apply.yml` de cada repositório lê a chave do próprio serviço no SSM e a grava no Secret do Kubernetes como `AWS_ACCESS_KEY_ID` e `AWS_SECRET_ACCESS_KEY`, que o SDK da AWS lê sozinho. Não há passo manual nem secret do GitHub para o SQS.

10. **Ambiente local com o ElasticMQ**: o `docker-compose.yml` de cada repositório sobe o ElasticMQ (`softwaremill/elasticmq-native:1.6.14`) com as quatro filas FIFO do `elasticmq.conf`. A API aponta para ele pelo `SqsSettings:ServiceUrl`, com credenciais fictícias (`local`).

**Consequências**:
- ✅ Um serviço fora do ar não derruba o outro: os eventos esperam na fila e são processados quando ele volta.
- ✅ Ordem garantida por OS, nova tentativa automática e DLQ, sem código de retry na aplicação.
- ✅ Sem pods novos e com custo perto de zero. Fila parada não é cobrada. O nível gratuito do SQS cobre 1 milhão de requisições por mês, e os dois consumidores, com *long polling* de 20 s, fazem cerca de 260 mil por mês (cada réplica a mais soma cerca de 130 mil). O SSM no tier Standard e o IAM não são cobrados.
- ✅ As camadas internas não conhecem o SQS: os casos de uso dependem só de `IEventosPublisher`, e os testes usam mocks, sem AWS nem Docker.
- ✅ As chaves são criadas junto com as filas, a cada ambiente recriado, e chegam aos pods sem passo manual.
- ⚠ **Consistência eventual:** a OS fica `Recebida` por alguns segundos antes de ser validada (ou cancelada), e gerar o orçamento responde 404 enquanto a Management não recebe o `OrdemServicoAberta`.
- ⚠ **Publicação depois de gravar:** se a publicação falhar, a alteração fica sem o evento. Exemplo: se o `OrcamentoAprovado` não sair, o orçamento fica aprovado e a OS continua em `AguardandoAprovacao`; dá para seguir pela rota `POST /api/ordens-servico/{id}/em-execucao` do OS Service.
- ⚠ **Uma mensagem com erro segura a OS:** até ela ir para a DLQ (cerca de 5 min), as mensagens seguintes da mesma OS esperam. A DLQ precisa ser analisada e reprocessada manualmente.
- ⚠ **Chave de acesso de longa duração:** o segredo fica no state do Terraform (bucket S3 privado e criptografado), no SSM e no Secret do Kubernetes. O ID da chave, mas não o segredo, aparece no log público do `terraform apply` no GitHub Actions. Risco aceito num ambiente de estudo, destruído depois da demonstração.
- ⚠ **Contratos duplicados:** cada repositório tem a própria cópia dos records. Mudar um evento exige mudar os dois repositórios, mantendo os nomes e a `versao`.
- ⚠ **Fluxo espalhado:** não há um lugar único com o estado da saga. Os logs `evento.*` e o `OrdemServicoId` ligam os passos nos dois serviços ([observabilidade.md](../observabilidade.md)).
- ⚠ **Sem integridade entre os bancos:** excluir na Management um cadastro usado por uma OS não é bloqueado (ADR-019).

**Alternativas Rejeitadas** (a comparação de transporte e de estilo está na [RFC-004](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md)):
- HTTP síncrono entre as APIs, SQS Standard, SNS + SQS, EventBridge, Kafka (Amazon MSK), RabbitMQ (Amazon MQ) e orquestração (AWS Step Functions ou um coordenador no OS Service):
  - rejeitadas pelos motivos da RFC-004.
- EKS Pod Identity para as credenciais:
  - rejeitada porque exige o agente do Pod Identity, um pod a mais em cada nó, num cluster já no limite (ADR-016).
- Access keys criadas à mão e guardadas como secrets do GitHub:
  - rejeitada por exigir passo manual e secrets novos a cada ambiente recriado, nos dois repositórios.
- AWS Secrets Manager no lugar do SSM:
  - rejeitada porque cada segredo é cobrado por mês; o SSM no tier Standard atende sem custo.
- Biblioteca compartilhada de contratos (pacote NuGet):
  - rejeitada por exigir publicação e versionamento de um pacote entre os repositórios; os eventos são poucos e simples.
- LocalStack no ambiente local:
  - rejeitada porque só o SQS precisa ser emulado, e o ElasticMQ é menor e mais simples.
- Fora do escopo, registrados como evoluções: *transactional outbox*, tabela de idempotência, SNS para *fan-out* (por exemplo, para serviços de faturamento e de execução), reprocessamento automático da DLQ, cópia dos cadastros no OS Service e bloqueio da exclusão de cadastros usados por uma OS.

**Relacionados**: [ADR-011](ADR-011-Envio-de-Email-de-Orcamento.md), [ADR-012](ADR-012-Controle-de-Estoque-de-Insumos.md), [ADR-014](ADR-014-Observabilidade-com-Datadog.md), [ADR-015](ADR-015-Comunicacao-Sincrona-via-API-Gateway.md), [ADR-016](ADR-016-Escalabilidade-com-HPA.md), [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md) (repo OS-SERVICE), [RFC-004](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md).
