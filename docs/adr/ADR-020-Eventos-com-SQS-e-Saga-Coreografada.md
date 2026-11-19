# ADR-020: Eventos com Amazon SQS e Saga Coreografada

**Status**: Aceita (fase 4). Atualizada ainda na fase 4 com a entrada do Billing ([ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md)): três serviços, *fan-out* no publicador e o evento `PagamentoAprovado` (ver **Atualização (Billing)**, abaixo). Atualizada de novo com a entrada do Execução e Produção ([ADR-023](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md)): quatro serviços, a regra de publicação reescrita e os eventos `DiagnosticoIniciado` e `ExecucaoFinalizada` (ver **Atualização (Execução)**, abaixo). Supera o item 7 da [ADR-015](ADR-015-Comunicacao-Sincrona-via-API-Gateway.md) (sem mensageria) e a alternativa de fila deixada como evolução futura na [ADR-011](ADR-011-Envio-de-Email-de-Orcamento.md)

**Resumo**:
A Management API, o OS Service ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)), o Billing ([ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md)) e o Execução e Produção ([ADR-023](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md)) conversam só por eventos em filas Amazon SQS FIFO, uma fila de entrada por serviço. Quem publica envia o mesmo evento para a fila de cada serviço que consome algum evento dele (*fan-out* no publicador). Não há chamadas HTTP entre as APIs. O fluxo da OS é uma saga coreografada: cada serviço grava a própria parte, publica um evento e reage aos eventos dos outros. Se a Management não aceita a OS, o OS Service a cancela (compensação). As opções avaliadas estão na [RFC-004](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md).

> As ADRs são numeradas em sequência única entre este monorepo e os repositórios [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658), [BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658), [EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice de todas as decisões está no [docs/arquitetura.md](../arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

**Contexto**:
- Com o OS Service, cada serviço tem o próprio banco, e nenhum lê o banco do outro (ADR-019). A OS guarda só os ids do cliente, do veículo e dos serviços. O orçamento, que ficava na Management, foi depois para o Billing, também com banco próprio (ADR-021).
- O OS Service precisa saber se a OS é válida, porque os cadastros são da Management, e quando o orçamento e o pagamento mudam. A Management precisa conhecer as OS, para validá-las, debitar o estoque e avisar o cliente. O Billing precisa dos serviços e dos preços da OS, que são da Management, para orçar.
- Depois, a fila de produção (diagnóstico e execução) foi para o Execução e Produção, também com banco próprio (ADR-023). Ele precisa conhecer as OS e o status delas, e o OS Service precisa saber quando o diagnóstico começa e quando a execução termina.
- Até a fase 3 não havia comunicação entre serviços nem mensageria (ADR-015, item 7).
- O cluster é pequeno (ADR-016), o custo precisa ficar perto de zero, e o ambiente é recriado com frequência: não pode haver passo manual nem secret novo a cada recriação.

**Decisão**:

1. **Quatro filas FIFO, uma de entrada por serviço**, criadas pelo Terraform em [SQS/infra](../../SQS/README.md):

   | Fila | Quem consome | Quem publica | DLQ |
   |---|---|---|---|
   | `techchallenge-oficina-management.fifo` | Management | OS Service e Billing | `techchallenge-oficina-management-dlq.fifo` |
   | `techchallenge-oficina-os-service.fifo` | OS Service | Management, Billing e Execução e Produção | `techchallenge-oficina-os-service-dlq.fifo` |
   | `techchallenge-oficina-billing.fifo` | Billing | Management e OS Service | `techchallenge-oficina-billing-dlq.fifo` |
   | `techchallenge-oficina-execution.fifo` | Execução e Produção | Management e OS Service | `techchallenge-oficina-execution-dlq.fifo` |

   - *Visibility timeout* de 60 s. Depois de 5 recebimentos sem sucesso (`maxReceiveCount`), a mensagem vai para a DLQ, que a guarda por 14 dias.
   - `MessageGroupId` = id da OS, o que garante a ordem entre os eventos de uma mesma OS, e `MessageDeduplicationId` = id do envelope. Não há deduplicação por conteúdo.
   - ***Fan-out* no publicador:** cada serviço publica nas filas dos serviços que consomem algum evento dele, pela lista `SqsSettings:FilasPublicacao` (variáveis `SqsSettings__FilasPublicacao__0`, `__1` e `__2`): a Management, nas do OS Service, do Billing e do Execução e Produção; o OS Service, nas da Management, do Billing e do Execução e Produção; o Billing, nas do OS Service e da Management (nenhum evento dele interessa ao Execução e Produção); o Execução e Produção, só na do OS Service. O envelope é montado uma vez, e o mesmo (com o mesmo id) vai para cada fila, uma por vez. Quem não trata um tipo de evento o ignora.

2. **Envelope JSON (camelCase)**, o mesmo em todos os sentidos:

   ```json
   {"id":"<guid>","tipo":"OrcamentoGerado","versao":1,"ocorridoEm":"2026-11-14T22:00:00Z","origem":"billing","dados":{"ordemServicoId":"<guid>","valorTotal":350.00}}
   ```

   - `origem` é `management`, `os-service`, `billing` ou `execution`. O `tipo` também vai no atributo de mensagem `tipo` do SQS.
   - Cada evento é um `record` C# na pasta `Features/Eventos/Contratos` do projeto UseCases. Cada repositório tem a própria cópia dos contratos que usa, sem biblioteca compartilhada.

3. **Eventos**:

   | Evento | De → para | Dados | O que quem consome faz |
   |---|---|---|---|
   | `OrdemServicoAberta` | OS Service → Management, Billing e Execução e Produção | `ordemServicoId`, `clienteId`, `veiculoId`, `servicoIds` | A Management valida os cadastros: grava a referência da OS e publica `OrdemServicoValidada`, ou publica `OrdemServicoRejeitada`. O Billing e o Execução e Produção ignoram |
   | `OrdemServicoAtualizada` | OS Service → Management, Billing e Execução e Produção | Os mesmos de `OrdemServicoAberta` | A Management faz a mesma validação, com os dados novos. O Billing e o Execução e Produção ignoram |
   | `OrdemServicoValidada` | Management → OS Service, Billing e Execução e Produção | `ordemServicoId`, `clienteId`, `veiculoId`, `clienteEmail`, `estoqueSuficiente`, `servicos` (`servicoId`, `nome` e `valor`) e `valorTotal` | O Billing guarda esses dados para orçar. O Execução e Produção guarda a OS, com os nomes dos serviços, para a fila de produção. O OS Service ignora |
   | `OrdemServicoRejeitada` | Management → OS Service, Billing e Execução e Produção | `ordemServicoId`, `motivo` | O OS Service cancela a OS (`Cancelada`) e guarda o motivo: é a compensação. O Billing apaga a referência da OS. O Execução e Produção ignora |
   | `OrdemServicoStatusAlterado` | OS Service → Management, Billing e Execução e Produção | `ordemServicoId`, `clienteId`, `statusAnterior`, `statusNovo` | A Management atualiza o status da referência e envia o e-mail de status (ADR-011). O Billing atualiza o status da referência dele. O Execução e Produção atualiza a cópia do status (com `EmExecucao`, a OS entra na fila de execução) |
   | `OrdemServicoExcluida` | OS Service → Management, Billing e Execução e Produção | `ordemServicoId` | A Management e o Billing apagam a referência (o Billing apaga também o orçamento e guarda os pagamentos), e o Execução e Produção tira a OS da fila de produção |
   | `OrcamentoGerado` | Billing → OS Service e Management | `ordemServicoId`, `valorTotal` | O OS Service leva a OS para `AguardandoAprovacao`, com o valor. Um orçamento gerado de novo só atualiza o valor. A Management ignora |
   | `OrcamentoRecusado` | Billing → OS Service e Management | `ordemServicoId` | O OS Service leva a OS para `Finalizada`. A Management ignora |
   | `PagamentoAprovado` | Billing → OS Service e Management | `ordemServicoId`, `pagamentoId`, `valor` | O OS Service leva a OS para `EmExecucao`. A Management debita o estoque da OS, uma vez só (ADR-012) |
   | `DiagnosticoIniciado` | Execução e Produção → OS Service | `ordemServicoId` | O OS Service leva a OS de `Recebida` para `EmDiagnostico` (etapa de log `diagnostico`) |
   | `ExecucaoFinalizada` | Execução e Produção → OS Service | `ordemServicoId` | O OS Service leva a OS de `EmExecucao` para `Finalizada` (etapa de log `finalizacao`) |

   Os status vão como números do enum `StatusOrdemServico`, iguais nos quatro serviços: `Recebida` 1, `EmDiagnostico` 2, `AguardandoAprovacao` 3, `EmExecucao` 4, `Finalizada` 5, `Entregue` 6, `Encerrada` 7 (sem uso) e `Cancelada` 8.

4. **Saga coreografada, com compensação** (diagramas na seção 4 da [arquitetura.md](../arquitetura.md#4-diagrama-de-sequência-abertura-de-ordem-de-serviço)):
   - O OS Service grava a OS como `Recebida` e publica `OrdemServicoAberta`. A Management confere se o cliente existe, se o veículo existe e é desse cliente e se os serviços existem.
     - **Válida:** a Management grava uma referência mínima da OS (`OrdemServicoReferencia`, com os ids, os serviços e o status), usada para o e-mail e para o débito de estoque. Depois publica `OrdemServicoValidada`, com os serviços e os preços, o e-mail do cliente e se há estoque, para o Billing orçar e para o Execução e Produção conhecer a OS.
     - **Inválida:** a Management publica `OrdemServicoRejeitada` com o motivo, e o OS Service cancela a OS.
   - **Orçamento e pagamento**, no Billing (ADR-021 e [ADR-022](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-022-Pagamentos-com-Mercado-Pago.md)): gerar → `OrcamentoGerado` → OS em `AguardandoAprovacao`; recusar → `OrcamentoRecusado` → OS em `Finalizada`; aprovar cria a cobrança no Mercado Pago, e o pagamento aprovado (por consulta ou pelo webhook) → `PagamentoAprovado` → OS em `EmExecucao` e débito do estoque na Management.
   - **Diagnóstico e execução**, no Execução e Produção (ADR-023): iniciar o diagnóstico → `DiagnosticoIniciado` → OS em `EmDiagnostico`; com a OS em `EmExecucao` (depois do `PagamentoAprovado`), ela entra na fila de execução; iniciar a execução só grava a data no Execução e Produção, sem evento; finalizar → `ExecucaoFinalizada` → OS em `Finalizada`. A entrega continua sendo uma rota do OS Service.
   - Só o OS Service muda o status da OS. Toda mudança de status no OS Service publica `OrdemServicoStatusAlterado`. A Management atualiza a referência e, nos status `EmDiagnostico`, `EmExecucao`, `Finalizada` e `Entregue`, envia o e-mail de status, que vem desligado (`SendEmailOnStatusChange=false`, ADR-011).

5. **Camada de mensageria, no mesmo padrão do e-mail (ADR-011)**:
   - A porta de saída `IEventosPublisher` fica no projeto UseCases (`Features/Eventos/Interfaces`).
   - A implementação fica no projeto `TechChallenge.Oficina.Messaging` (no OS Service, `TechChallenge.Oficina.OsService.Messaging`, no Billing, `TechChallenge.Oficina.Billing.Messaging`, e no Execução e Produção, `TechChallenge.Oficina.Execution.Messaging`), no anel "1 - Frameworks & Drivers":
     - `SqsSettings` (`FilaConsumo`, `FilasPublicacao` e `ServiceUrl`);
     - `ISqsClient`, com o `SqsClientAdapter`, o único ponto que usa o SDK (`AWSSDK.SQS` 4.0.x);
     - `EventosPublisher`, `SqsConsumidorHostedService` e o registro `AddInfraMessaging`.
   - **Entrada pelo controller:** o consumidor lê o envelope e chama o `EventosController` (projeto Controllers) com o `tipo` e os `dados`, e o controller chama o caso de uso. É o mesmo caminho de uma requisição HTTP: endpoint → controller → caso de uso.

6. **Consumo, nova tentativa e DLQ**:
   - O consumidor é um `BackgroundService` com *long polling* de 20 s, lotes de até 10 mensagens e um escopo de injeção de dependência por mensagem, como numa requisição.
   - Evento processado ou ignorado: a mensagem é apagada.
   - Exceção: a mensagem não é apagada, volta para a fila depois do *visibility timeout* e, no 5º recebimento, vai para a DLQ. Não há reprocessamento automático da DLQ.
   - Depois de uma falha, os quatro consumidores param o lote: as mensagens seguintes também voltam para a fila, para não passarem na frente da que falhou (ordem por OS).

7. **Idempotência sem tabela extra**: evento repetido, transição que o status atual não permite, OS desconhecida e tipo que o serviço não trata são ignorados (log `evento.ignorado`), sem exceção e sem publicar nada. Exemplos: um `PagamentoAprovado` repetido encontra a OS já `EmExecucao` no OS Service e o estoque já debitado na Management (coluna `estoque_debitado`); o `OrdemServicoStatusAlterado` de uma OS rejeitada não encontra a referência na Management.

8. **Publicar depois de gravar**: cada serviço grava no próprio banco e só então publica. Se a publicação falhar, sai o log `integracao.falha` (`Integracao=sqs`, `Operacao=publicacao`, com a `Fila`) e a exceção sobe: numa requisição, ela responde 500, mas a alteração gravada continua; no consumo de um evento, a mensagem volta para a fila. No *fan-out*, as filas anteriores à que falhou já receberam o evento (entrega parcial), e a nova tentativa pode entregá-lo de novo, com outro id: os consumidores idempotentes absorvem a repetição. É um risco aceito; a evolução é o *transactional outbox*.

9. **Credenciais: um usuário IAM por serviço, com a chave no SSM**:
   - O `SQS/infra` cria os usuários `techchallenge-oficina-management-sqs`, `techchallenge-oficina-os-service-sqs`, `techchallenge-oficina-billing-sqs` e `techchallenge-oficina-execution-sqs`, com policy inline de menor privilégio: consumir a própria fila e só publicar (`SendMessage` e `GetQueueUrl`) nas filas da lista `publishes_to` (as dos serviços que consomem algum evento dele).
   - A access key de cada usuário é criada pelo Terraform e guardada no SSM Parameter Store, como SecureString no tier Standard (sem custo), em `/techchallenge-oficina/sqs/<management|os-service|billing|execution>/access-key-id` e `/secret-access-key`.
   - O `_k8s-apply.yml` de cada repositório lê a chave do próprio serviço no SSM e a grava no Secret do Kubernetes como `AWS_ACCESS_KEY_ID` e `AWS_SECRET_ACCESS_KEY`, que o SDK da AWS lê sozinho. Não há passo manual nem secret do GitHub para o SQS.

10. **Ambiente local com o ElasticMQ**: o `docker-compose.yml` de cada repositório sobe o ElasticMQ (`softwaremill/elasticmq-native:1.6.14`) com as filas FIFO do `elasticmq.conf`: as oito (as quatro filas e as DLQs) na Management e no OS Service; o Billing manteve as seis de antes, porque não usa a fila do Execução e Produção. A API aponta para ele pelo `SqsSettings:ServiceUrl`, com credenciais fictícias (`local`). Para a saga local, os quatro serviços usam o ElasticMQ da Management.

**Consequências**:
- ✅ Um serviço fora do ar não derruba os outros: os eventos esperam na fila e são processados quando ele volta.
- ✅ Ordem garantida por OS, nova tentativa automática e DLQ, sem código de retry na aplicação.
- ✅ Sem pods novos e com custo perto de zero. Fila parada não é cobrada. O nível gratuito do SQS cobre 1 milhão de requisições por mês, e os quatro consumidores, com *long polling* de 20 s, fazem cerca de 520 mil por mês (cada réplica a mais soma cerca de 130 mil). O SSM no tier Standard e o IAM não são cobrados.
- ✅ As camadas internas não conhecem o SQS: os casos de uso dependem só de `IEventosPublisher`, e os testes usam mocks, sem AWS nem Docker.
- ✅ As chaves são criadas junto com as filas, a cada ambiente recriado, e chegam aos pods sem passo manual.
- ⚠ **Consistência eventual:** a OS fica `Recebida` por alguns segundos antes de ser validada (ou cancelada), e o Billing só gera o orçamento depois de receber o `OrdemServicoValidada`.
- ⚠ **Publicação depois de gravar:** se a publicação falhar, a alteração fica sem o evento. Exemplo: se o `PagamentoAprovado` não sair, o pagamento fica aprovado no Billing e a OS continua em `AguardandoAprovacao`. O OS Service não tem rota para levar a OS para `EmExecucao` (ela só entra em execução pelo `PagamentoAprovado`, e o pagamento não pode ser pulado): o caso fica para tratamento manual.
- ⚠ **Uma mensagem com erro segura a OS:** até ela ir para a DLQ (cerca de 5 min), as mensagens seguintes da mesma OS esperam. A DLQ precisa ser analisada e reprocessada manualmente.
- ⚠ **Ordem só dentro de cada fila:** eventos de serviços diferentes sobre a mesma OS podem chegar em qualquer ordem. Exemplo: o Billing pode receber o `OrdemServicoStatusAlterado` antes do `OrdemServicoValidada`, e trata esse caso (ADR-021); o Execução e Produção também (ADR-023).
- ⚠ **Chave de acesso de longa duração:** o segredo fica no state do Terraform (bucket S3 privado e criptografado), no SSM e no Secret do Kubernetes. O ID da chave, mas não o segredo, aparece no log público do `terraform apply` no GitHub Actions. Risco aceito num ambiente de estudo, destruído depois da demonstração.
- ⚠ **Contratos duplicados:** cada repositório tem a própria cópia dos records. Mudar um evento exige mudar os repositórios que o publicam e o consomem, mantendo os nomes e a `versao`.
- ⚠ **Fluxo espalhado:** não há um lugar único com o estado da saga. Os logs `evento.*` e o `OrdemServicoId` ligam os passos nos quatro serviços ([observabilidade.md](../observabilidade.md)).
- ⚠ **Sem integridade entre os bancos:** excluir na Management um cadastro usado por uma OS não é bloqueado (ADR-019).

**Atualização (Billing)**:

Na versão original, eram dois serviços e duas filas: o orçamento ficava na Management, que publicava `OrcamentoGerado`, `OrcamentoAprovado` e `OrcamentoRecusado`, e debitava o estoque ao aprovar. O *fan-out* para um terceiro serviço ficou registrado como evolução, com um tópico SNS. Com a saída do orçamento para o Billing (ADR-021):
- entrou a terceira fila (`techchallenge-oficina-billing.fifo`, com DLQ e usuário IAM), e o `publishes_to` de cada serviço virou uma lista com as filas dos outros dois;
- o *fan-out* ficou no publicador (`FilasPublicacao`), e não no SNS: é mais simples (sem tópico, assinaturas e políticas), funciona no ElasticMQ do ambiente local, que não emula o SNS, e mantém a permissão de cada usuário IAM por fila. O custo é que quem publica precisa conhecer as filas de destino: um serviço novo exige um item a mais na lista e na policy de cada publicador;
- entraram o `OrdemServicoValidada`, que leva ao Billing os serviços e os preços (calculados pela Management com `Servico.CalcularValor()`) e o resultado do estoque (`estoqueSuficiente`), e o `PagamentoAprovado`, que **substitui o `OrcamentoAprovado`**: a OS só entra em execução com o pagamento aprovado, e o débito de estoque passou a acontecer nesse momento (ADR-012);
- `OrcamentoGerado` e `OrcamentoRecusado` continuam com os mesmos nomes e dados, agora publicados pelo Billing.

**Atualização (Execução)**:

Com a entrada do Execução e Produção (ADR-023), que cuida da fila de produção da oficina, do diagnóstico e da execução:
- entrou a quarta fila (`techchallenge-oficina-execution.fifo`, com DLQ e o usuário IAM `techchallenge-oficina-execution-sqs`);
- **a regra de publicação foi reescrita.** Antes, cada serviço publicava nas filas de todos os outros. Agora, cada serviço publica nas filas dos serviços que consomem algum evento dele: a Management e o OS Service publicam nas filas dos outros três; o Billing continua publicando só nas do OS Service e da Management, porque nenhum evento dele interessa ao Execução e Produção; e o Execução e Produção publica só na do OS Service. O *fan-out* continua no publicador, com o mesmo envelope (mesmo id) em cada fila, e o `publishes_to` de cada usuário IAM segue a mesma lista;
- entraram os eventos `DiagnosticoIniciado` e `ExecucaoFinalizada` (Execução e Produção → OS Service, `origem` = `execution`). Não há evento para o início da execução: ele só registra a data e o log no Execução e Produção;
- as rotas `POST /api/ordens-servico/{id}/em-diagnostico`, `/em-execucao` e `/finalizar` saíram do OS Service: o diagnóstico e a finalização passaram para o Execução e Produção, e a execução só começa pelo `PagamentoAprovado`. O status da OS continua sendo só do OS Service, que agora o muda ao consumir os eventos novos. O Execução e Produção conhece a OS pelo `OrdemServicoValidada` e guarda uma cópia do status, alimentada pelo `OrdemServicoStatusAlterado`;
- a ordem de subida passou a importar: o OS Service precisa consumir os eventos novos antes de o Execução e Produção publicá-los (Bootstrap do OS-SERVICE antes do EXECUTION);
- a Management não mudou de código: só passou a publicar também na fila nova (`SqsSettings__FilasPublicacao__2`).

**Alternativas Rejeitadas** (a comparação de transporte e de estilo está na [RFC-004](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md)):
- HTTP síncrono entre as APIs, SQS Standard, SNS + SQS, EventBridge, Kafka (Amazon MSK), RabbitMQ (Amazon MQ) e orquestração (AWS Step Functions ou um coordenador no OS Service):
  - rejeitadas pelos motivos da RFC-004.
- Tópico SNS FIFO na frente das filas para o *fan-out*, quando o Billing entrou:
  - rejeitado pelos motivos da seção Atualização: mais peças para criar e configurar, sem emulação no ElasticMQ, para só três serviços. Com o Execução e Produção, continua rejeitado: são quatro serviços, e cada um publica em no máximo três filas.
- Manter a regra antiga (publicar nas filas de todos os outros serviços) com o Execução e Produção:
  - rejeitada porque mandaria para a fila dele os eventos do Billing, que ele não usa, e para as filas da Management e do Billing os eventos dele, que só o OS Service usa. Com a regra nova, a policy de cada usuário IAM também libera só as filas necessárias.
- EKS Pod Identity para as credenciais:
  - rejeitada porque exige o agente do Pod Identity, um pod a mais em cada nó, num cluster já no limite (ADR-016).
- Access keys criadas à mão e guardadas como secrets do GitHub:
  - rejeitada por exigir passo manual e secrets novos a cada ambiente recriado, em cada repositório.
- AWS Secrets Manager no lugar do SSM:
  - rejeitada porque cada segredo é cobrado por mês; o SSM no tier Standard atende sem custo.
- Biblioteca compartilhada de contratos (pacote NuGet):
  - rejeitada por exigir publicação e versionamento de um pacote entre os repositórios; os eventos são poucos e simples.
- LocalStack no ambiente local:
  - rejeitada porque só o SQS precisa ser emulado, e o ElasticMQ é menor e mais simples.
- Fora do escopo, registrados como evoluções: *transactional outbox*, tabela de idempotência, reprocessamento automático da DLQ, cópia dos cadastros no OS Service e bloqueio da exclusão de cadastros usados por uma OS.

**Relacionados**: [ADR-011](ADR-011-Envio-de-Email-de-Orcamento.md), [ADR-012](ADR-012-Controle-de-Estoque-de-Insumos.md), [ADR-014](ADR-014-Observabilidade-com-Datadog.md), [ADR-015](ADR-015-Comunicacao-Sincrona-via-API-Gateway.md), [ADR-016](ADR-016-Escalabilidade-com-HPA.md), [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md) (repo OS-SERVICE), [ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md) e [ADR-022](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-022-Pagamentos-com-Mercado-Pago.md) (repo BILLING), [ADR-023](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md) (repo EXECUTION), [RFC-004](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md).
