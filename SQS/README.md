# SQS: filas da comunicação entre os serviços

Infraestrutura das filas do projeto **Tech Challenge Oficina** (SOAT16, fase 4). A Management API (este monorepo), o OS Service ([repo OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658)), o Billing ([repo BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658)) e o Execução e Produção ([repo EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658)) conversam só por eventos nessas filas, numa saga coreografada ([ADR-020](../docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)). As opções comparadas estão na [RFC-004](../docs/rfc/RFC-004-Comunicacao-entre-Microsservicos.md).

Tudo é criado com Terraform em [infra/](infra) e aplicado pelas pipelines do monorepo ([.github/workflows](../.github/workflows/README.md)). A pasta tem o próprio state e não depende da VPC nem do EKS: pode ser aplicada antes deles.

## 1. O que é provisionado

Tudo fica na AWS, região `us-east-1`. Cada serviço tem uma fila de entrada, uma DLQ e um usuário IAM ([locals.tf](infra/locals.tf)):

| Recurso | Management | OS Service | Billing | Execução e Produção |
|---|---|---|---|---|
| Fila FIFO de entrada (`aws_sqs_queue`) | `techchallenge-oficina-management.fifo` | `techchallenge-oficina-os-service.fifo` | `techchallenge-oficina-billing.fifo` | `techchallenge-oficina-execution.fifo` |
| DLQ FIFO | `techchallenge-oficina-management-dlq.fifo` | `techchallenge-oficina-os-service-dlq.fifo` | `techchallenge-oficina-billing-dlq.fifo` | `techchallenge-oficina-execution-dlq.fifo` |
| Usuário IAM (`aws_iam_user`) | `techchallenge-oficina-management-sqs` | `techchallenge-oficina-os-service-sqs` | `techchallenge-oficina-billing-sqs` | `techchallenge-oficina-execution-sqs` |
| Access key (SSM, SecureString) | `/techchallenge-oficina/sqs/management/access-key-id` e `/secret-access-key` | `/techchallenge-oficina/sqs/os-service/access-key-id` e `/secret-access-key` | `/techchallenge-oficina/sqs/billing/access-key-id` e `/secret-access-key` | `/techchallenge-oficina/sqs/execution/access-key-id` e `/secret-access-key` |
| Publica nas filas de (`publishes_to`) | OS Service, Billing e Execução e Produção | Management, Billing e Execução e Produção | OS Service e Management | OS Service |

Configuração das filas ([sqs.tf](infra/sqs.tf)):
- **Fila de entrada:** FIFO, sem deduplicação por conteúdo (quem publica informa o `MessageGroupId` e o `MessageDeduplicationId`) e *visibility timeout* de 60 s.
- **DLQ:** recebe a mensagem depois de 5 recebimentos sem sucesso (`maxReceiveCount`) e a guarda por 14 dias, o máximo do SQS. Só a fila do próprio serviço pode usá-la (*redrive allow policy*).
- ***Fan-out* no publicador:** não há tópico SNS. Cada serviço envia o mesmo evento, com o mesmo id, para as filas dos serviços que consomem algum evento dele, uma por vez (`publishes_to` aqui e `SqsSettings:FilasPublicacao` na aplicação). Por isso, o Billing não publica na fila do Execução e Produção, e o Execução e Produção publica só na do OS Service. Quem recebe um tipo de evento que não trata o ignora ([ADR-020](../docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)).

```mermaid
flowchart LR
  OS[OS Service\nos-service-api] -->|publica| FilaM[[techchallenge-oficina-management.fifo]]
  BIL[Billing\nbilling-api] -->|publica| FilaM
  FilaM -->|consome| M[Management API\nmanagement-api]
  M -->|publica| FilaOS[[techchallenge-oficina-os-service.fifo]]
  BIL -->|publica| FilaOS
  EXE[Execucao e Producao\nexecution-api] -->|publica| FilaOS
  FilaOS -->|consome| OS
  M -->|publica| FilaB[[techchallenge-oficina-billing.fifo]]
  OS -->|publica| FilaB
  FilaB -->|consome| BIL
  M -->|publica| FilaE[[techchallenge-oficina-execution.fifo]]
  OS -->|publica| FilaE
  FilaE -->|consome| EXE
  FilaM -.->|5 falhas| DLQM[[...-management-dlq.fifo]]
  FilaOS -.->|5 falhas| DLQOS[[...-os-service-dlq.fifo]]
  FilaB -.->|5 falhas| DLQB[[...-billing-dlq.fifo]]
  FilaE -.->|5 falhas| DLQE[[...-execution-dlq.fifo]]
```

### 1.1 Permissões dos usuários IAM

Cada usuário tem uma policy inline chamada `sqs`, de menor privilégio ([iam.tf](infra/iam.tf)):

| Statement | Ações | Recurso |
|---|---|---|
| `ConsumirFilaPropria` | `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility`, `sqs:GetQueueAttributes` e `sqs:GetQueueUrl` | A fila de entrada do próprio serviço |
| `PublicarNasFilasDosOutrosServicos` | `sqs:SendMessage` e `sqs:GetQueueUrl` | As filas de entrada listadas em `publishes_to` (as dos serviços que consomem algum evento dele) |

Nenhum dos usuários acessa as DLQs: analisar e reprocessar uma mensagem da DLQ é feito com um usuário administrativo (seção 7).

Os quatro usuários também recebem uma segunda policy inline, para a própria tabela DynamoDB (só `dynamodb:PutItem` e `dynamodb:Query`): a mesma chave serve às filas e à tabela. A da Management é criada aqui, junto do usuário; as dos outros três, pelo `DB/infra` do repositório do serviço.

| Usuário | Policy | Tabela | Criada por |
|---|---|---|---|
| `techchallenge-oficina-management-sqs` | `dynamodb-historico-estoque` | `techchallenge-oficina-management-estoque-historico` (histórico de movimentações de estoque) | Este repo, `SQS/infra/iam.tf` ([ADR-027](../docs/adr/ADR-027-Historico-de-Movimentacoes-de-Estoque-no-DynamoDB.md)) |
| `techchallenge-oficina-os-service-sqs` | `dynamodb-historico-saga` | `techchallenge-oficina-os-saga-historico` (histórico da saga) | Repo OS-SERVICE ([ADR-026](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-026-Historico-da-Saga-no-DynamoDB.md)) |
| `techchallenge-oficina-billing-sqs` | `dynamodb-historico-pagamentos` | `techchallenge-oficina-billing-pagamentos-historico` (histórico de pagamentos) | Repo BILLING ([ADR-024](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-024-Historico-de-Pagamentos-no-DynamoDB.md)) |
| `techchallenge-oficina-execution-sqs` | `dynamodb-historico-producao` | `techchallenge-oficina-execution-producao-historico` (linha do tempo da produção) | Repo EXECUTION ([ADR-025](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-025-Linha-do-Tempo-da-Producao-no-DynamoDB.md)) |

- **Management:** a policy é gerenciada aqui ([iam.tf](infra/iam.tf)), no mesmo state do usuário, e não no `DB/infra` deste monorepo, onde fica a tabela: o `DB/infra` e o `SQS/infra` rodam em paralelo nas pipelines, e uma policy lá correria contra a criação do usuário. Se o usuário for recriado, a policy volta no mesmo apply. O ARN da tabela é montado pelo nome, repetido no [locals.tf](infra/locals.tf): ele precisa ser igual ao do [DB/infra/dynamodb.tf](../DB/infra/dynamodb.tf).
- **OS Service, Billing e Execução e Produção:** as policies vêm dos repositórios deles e não são gerenciadas aqui. Se este Terraform recriar um desses usuários, a policy dele some junto, e é preciso rodar de novo o Bootstrap do OS-SERVICE, do BILLING ou do EXECUTION (o Deploy deles só reaplica o `DB/infra` quando os arquivos mudam).

### 1.2 Chaves no SSM e no Secret dos pods

- O Terraform cria uma access key para cada usuário e guarda o ID e o segredo no SSM Parameter Store, como SecureString no tier Standard ([ssm.tf](infra/ssm.tf)).
- O `_k8s-apply.yml` **de cada repositório** lê só os parâmetros do próprio serviço e grava os valores no Secret do Kubernetes como `AWS_ACCESS_KEY_ID` e `AWS_SECRET_ACCESS_KEY`, mascarados no log. A mesma chave serve às filas e à tabela DynamoDB do serviço. O SDK da AWS lê essas variáveis sozinho, e a região vem de `AWS_REGION`, no ConfigMap.
  - Management: Secret `oficina-api-secrets`, namespace `oficina` ([kubernetes/k8s/README.md](../kubernetes/k8s/README.md)).
  - OS Service: Secret `os-service-api-secrets`, namespace `os-service` (repo OS-SERVICE).
  - Billing: Secret `billing-api-secrets`, namespace `billing` (repo BILLING).
  - Execução e Produção: Secret `execution-api-secrets`, namespace `execution` (repo EXECUTION).
- Não há passo manual nem secret do GitHub para o SQS. O Destroy apaga as filas, os usuários e as chaves, e o Bootstrap seguinte cria tudo de novo, com chaves novas.

Outputs do Terraform (sem segredos): `queue_urls`, `queue_arns`, `dlq_urls`, `iam_user_names` e `ssm_parameter_names`.

## 2. Contrato com os serviços

| Item | Valor | Onde é usado |
|---|---|---|
| Fila consumida pela Management | `techchallenge-oficina-management.fifo` | `SqsSettings__FilaConsumo` da Management e um item da lista `SqsSettings__FilasPublicacao__*` do OS Service e do Billing (ConfigMaps) |
| Fila consumida pelo OS Service | `techchallenge-oficina-os-service.fifo` | `SqsSettings__FilaConsumo` do OS Service e um item da lista `SqsSettings__FilasPublicacao__*` da Management, do Billing e do Execução e Produção |
| Fila consumida pelo Billing | `techchallenge-oficina-billing.fifo` | `SqsSettings__FilaConsumo` do Billing e um item da lista `SqsSettings__FilasPublicacao__*` da Management e do OS Service |
| Fila consumida pelo Execução e Produção | `techchallenge-oficina-execution.fifo` | `SqsSettings__FilaConsumo` do Execução e Produção e um item da lista `SqsSettings__FilasPublicacao__*` da Management e do OS Service (`__2` nos dois) |
| Parâmetros SSM | `/techchallenge-oficina/sqs/<management\|os-service\|billing\|execution>/access-key-id` e `/secret-access-key` | `_k8s-apply.yml` de cada repositório |
| Grupo e deduplicação | `MessageGroupId` = id da OS; `MessageDeduplicationId` = id do envelope, o mesmo em todas as filas de destino | Publicadores dos quatro serviços |
| Envelope e eventos | JSON camelCase com `id`, `tipo`, `versao`, `ocorridoEm`, `origem` e `dados`; atributo de mensagem `tipo` | [ADR-020](../docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) |

Os nomes são fixos: mudar uma fila ou um caminho do SSM exige mudar os ConfigMaps e os `_k8s-apply.yml` dos quatro repositórios.

## 3. Pipelines (GitHub Actions)

O SQS não tem workflows próprios: roda nas pipelines do monorepo, pelo workflow reutilizável `_terraform.yml` com `working-directory: SQS/infra`. Os detalhes estão no [README das pipelines](../.github/workflows/README.md).

| Workflow | O que faz com o SQS |
|---|---|
| **Bootstrap** (manual) | `terraform-sqs` (apply), logo depois dos testes e em paralelo com a foundation. O `k8s-apply` espera por ele |
| **Deploy**, em Pull Request | `validate-sqs` (sem AWS, check obrigatório `validate-sqs / terraform`) e `plan-sqs` (informativo; não depende do cluster, mas não roda em PR de fork, que não recebe secrets) |
| **Deploy**, em push na `main` com mudança em `SQS/infra/**` ou no `_terraform.yml` | `apply-sqs` e, em seguida, o `apply-k8s` com restart, para o Secret receber a chave atual |
| **Destroy** (manual, `confirm = destroy` + aprovação) | `terraform-destroy-sqs`, em paralelo com o destroy do RDS |

Os repos OS-SERVICE, BILLING e EXECUTION não aplicam este Terraform: o `_k8s-apply.yml` de cada um só lê do SSM a chave do próprio serviço. Por isso, o Bootstrap deste monorepo precisa rodar antes dos Bootstraps do OS-SERVICE, do BILLING e do EXECUTION.

| Pasta | State (bucket `terraform-state-soat16`) |
|---|---|
| `SQS/infra` | `techchallenge-oficina/fase-4/sqs.tfstate`, com lock nativo (`use_lockfile`) |

**Permissões do usuário IAM `terraform`** (o mesmo das outras pastas): além do state em `techchallenge-oficina/fase-4/*`, ele precisa criar e apagar filas SQS, usuários IAM com policy inline e access key, e parâmetros SecureString no SSM. A policy `dynamodb-historico-estoque` da Management usa `iam:PutUserPolicy`, `iam:GetUserPolicy` e `iam:DeleteUserPolicy` (hoje o usuário é administrador). O apply dos manifests lê os parâmetros com `ssm:GetParameter` (com `--with-decryption`).

## 4. Custo

- Fila parada não é cobrada. O nível gratuito do SQS cobre 1 milhão de requisições por mês.
- Os quatro consumidores fazem *long polling* de 20 s: cerca de 130 mil requisições por mês por pod, ou cerca de 520 mil com uma réplica de cada API. Publicações (com o *fan-out*, uma por fila de destino) e exclusões somam pouco.
- O SSM Parameter Store no tier Standard e o IAM não são cobrados.

## 5. Uso local (ElasticMQ)

Localmente não há SQS: o `docker-compose.yml` da raiz sobe o ElasticMQ (`softwaremill/elasticmq-native:1.6.14`), que emula o SQS na porta `9324`. O [elasticmq.conf](../elasticmq.conf) cria as mesmas oito filas FIFO (as quatro filas e as DLQs), com a DLQ depois de 5 recebimentos (lá o *visibility timeout* é de 30 s).

- A API usa o `SqsSettings:ServiceUrl` (`http://elasticmq:9324` no compose, `http://localhost:9324` no `dotnet run`) e credenciais fictícias (`local`), que o ElasticMQ aceita.
- Para rodar a saga com o OS Service, o Billing e o Execução e Produção, os quatro serviços precisam usar o mesmo ElasticMQ, o deste repositório (container `oficina-elasticmq`):
  1. Neste repositório: `podman compose up -d` (ou `docker compose up -d`).
  2. Nos repositórios OS-SERVICE, BILLING e EXECUTION: `SQS_SERVICE_URL=http://oficina-elasticmq:9324 podman compose up -d --build` (PowerShell: `$env:SQS_SERVICE_URL="http://oficina-elasticmq:9324"; podman compose up -d --build`).
  3. Ligue os containers `oficina-os-service-api`, `oficina-billing-api` e `oficina-execution-api` à rede do compose deste repositório: `podman network connect tech-challenge-fase-4-management-soat16-rm374658_default <container>` (no Docker, `docker network connect ...`). O nome da rede vem do nome da pasta deste repositório (o nome do projeto do compose), mais `_default`.

Para validar o Terraform sem acessar a AWS, a partir de `SQS/infra/`:

```powershell
terraform init -backend=false
terraform fmt -check -recursive
terraform validate
```

O `.terraform.lock.hcl` **é** versionado e fixa o provider AWS (6.66.0).

## 6. Riscos aceitos

- **O segredo da access key fica no state** do Terraform (bucket S3 privado e criptografado), como a senha do RDS, além do SSM e do Secret dos pods.
- **O ID da access key aparece no log do `terraform apply`**, que é público no GitHub Actions (o segredo não aparece). Num ambiente de estudo, destruído depois, o risco foi aceito.
- **Chaves de longa duração**: só mudam quando os usuários ou as chaves são recriados pelo Terraform (por exemplo, num Destroy seguido de Bootstrap).

## 7. Troubleshooting

- **Apply dos manifests falha em "Adicionar a chave do usuário IAM da Management (SQS e DynamoDB) ao kubernetes/k8s/.env"** (no OS-SERVICE, no BILLING e no EXECUTION, o passo se chama "Adicionar a chave do usuário IAM do OS Service (SQS e DynamoDB)", "Adicionar a chave do usuário IAM do Billing (SQS e DynamoDB)" e "Adicionar a chave do usuário IAM do Execução e Produção (SQS e DynamoDB)") (`Parâmetro ... não encontrado no SSM`): o `SQS/infra` ainda não foi aplicado. Rode o Bootstrap deste monorepo.
- **Pods com `integracao.falha` (`sqs`) e erro de credencial no `error.*`**: o Secret tem uma chave que não existe mais, porque o SQS foi recriado depois do último apply dos manifests. O Deploy e o Bootstrap deste monorepo já atualizam o Secret da Management; os do OS Service, do Billing e do Execução e Produção, não. Rode o K8s Apply dos repos OS-SERVICE, BILLING e EXECUTION (ou o do repositório afetado) com `restart-pods = true`.
- **OS Service, Billing ou Execução e Produção com `integracao.falha` (`dynamodb`) e acesso negado no `error.*`**: o usuário IAM do serviço foi recriado aqui e perdeu a policy do DynamoDB, que vem do repositório do serviço. Rode o Bootstrap do OS-SERVICE, do BILLING ou do EXECUTION (ele reaplica a policy e os manifests com a chave nova).
- **Management com `integracao.falha` (`dynamodb`) e acesso negado no `error.*`**: confira se a policy `dynamodb-historico-estoque` está no usuário `techchallenge-oficina-management-sqs` e se o nome da tabela no `SQS/infra` é o mesmo do `DB/infra`. A policy vem deste Terraform, junto do usuário: rode o Bootstrap deste monorepo, que reaplica o `SQS/infra`, o `DB/infra` e os manifests.
- **Mensagens paradas na DLQ**: veja o motivo nos logs `integracao.falha` com `Operacao` = `consumo` ([docs/observabilidade.md](../docs/observabilidade.md)). Depois de corrigir a causa, reenvie as mensagens para a fila de origem pelo console do SQS (redrive da DLQ), com um usuário administrativo. Não há reprocessamento automático.
- **`Error acquiring the state lock`**: outro apply ou destroy está rodando. Se o lock ficou preso depois de um run cancelado, rode `terraform force-unlock <LOCK_ID>` em `SQS/infra/` ou apague o objeto `techchallenge-oficina/fase-4/sqs.tfstate.tflock` do bucket.
