# Infraestrutura AWS com Terraform

## Objetivo

Provisionar a rede e o Amazon EKS onde rodam a Management API, o OS Service (repo OS-SERVICE), o Billing (repo BILLING) e o Execução e Produção (repo EXECUTION) e expô-los na Internet por HTTPS, pelo endpoint padrão (`execute-api`) de um Amazon API Gateway.

O Terraform gerencia a infraestrutura AWS, inclusive o API Gateway (state `foundation`), e, num state separado (`addons`), instala o Metrics Server e o Datadog Agent via Helm e cria os recursos do Datadog (módulo local `addons/modules/datadog/`). Os manifests da Management API ([k8s/](../k8s/kustomization.yaml)) são aplicados pelo GitHub Actions com `kubectl apply -k kubernetes/k8s` (seção [Aplicação dos manifests](#aplicação-dos-manifests)); os do OS Service, os do Billing e os do Execução e Produção, pelas pipelines dos repos OS-SERVICE, BILLING e EXECUTION.

O banco de dados (RDS PostgreSQL) e a tabela DynamoDB do histórico de movimentações de estoque não são criados aqui: ficam na pasta [DB/](../../DB/README.md) do monorepo, e os do OS Service, do Billing e do Execução e Produção, nos repos OS-SERVICE, BILLING e EXECUTION. As filas SQS ficam na pasta [SQS/](../../SQS/README.md). O build da imagem (a partir de `server/`) e o push para o ECR ficam nas pipelines da raiz ([.github/workflows/README.md](../../.github/workflows/README.md)).

## Decisões consolidadas

- Região: `us-east-1`.
- Bucket de state externo: `terraform-state-soat16`, com lock nativo (`use_lockfile`).
- States:
  - `techchallenge-oficina/fase-4/k8s-foundation.tfstate`.
  - `techchallenge-oficina/fase-4/k8s-addons.tfstate`.
- Os arquivos `.terraform.lock.hcl` são versionados: provider AWS `6.66.0` nas duas pastas, e Helm `2.17.0` e DataDog/datadog `4.25.0` no addons.
- Observabilidade com Datadog (conta trial): o Agent (Helm) e os recursos do Datadog (métricas de log, monitores, synthetics, SLO e dashboard) ficam em Terraform, no addons, e são ligados ou desligados pela variable `DD_ENABLED` do GitHub. Detalhes em [DATADOG.md](DATADOG.md).
- Sem Route 53, domínio próprio ou certificado próprio: o acesso usa o endpoint padrão do API Gateway (`https://<id>.execute-api.us-east-1.amazonaws.com`), que só atende HTTPS.
- O API Gateway é uma HTTP API com um Lambda authorizer (repo LAMBDA) nas rotas das APIs (`/management/...`, `/os/...`, `/billing/...` e `/execution/...`). Ele exige um JWT válido do Auth0 com a claim `cpf`; o cadastro de cliente (`POST /management/api/clientes`) aceita JWT sem `cpf`. As APIs continuam validando o JWT. Ficam públicas, sem authorizer, as rotas `GET /management/health`, `GET /os/health`, `GET /billing/health`, `GET /execution/health`, `POST /billing/api/pagamentos/notificacoes` (webhook do Mercado Pago, que não envia o JWT) e `POST /auth/token` (Lambda que gera o JWT com `cpf`).
- Não há Ingress nem Ingress Controller. O API Gateway chega aos pods por VPC Link → NLB interno → NodePort, tudo criado pelo Terraform da foundation. Nenhum Load Balancer é criado pelo Kubernetes.
- Os repositórios ECR `techchallenge-oficina-management` (este monorepo), `techchallenge-oficina-os-service` (repo OS-SERVICE), `techchallenge-oficina-billing` (repo BILLING) e `techchallenge-oficina-execution` (repo EXECUTION) são criados manualmente, e as imagens são publicadas pelas pipelines de cada repositório. Aqui, os nodes só leem do ECR pela role IAM com `AmazonEC2ContainerRegistryReadOnly`; não existe Secret Kubernetes para pull.
- `apply` e `destroy` rodam pelo GitHub Actions e só a partir da `main` (detalhes em [.github/workflows/README.md](../../.github/workflows/README.md)).
- O ambiente é acadêmico e descartável; todos os recursos podem ser destruídos ao final.

## Pré-requisitos manuais

Antes do primeiro Bootstrap, devem existir:

1. Bucket S3 `terraform-state-soat16`, em `us-east-1`, com versionamento, criptografia, bloqueio público e leitura/escrita nas keys `techchallenge-oficina/fase-4/*` (`arn:aws:s3:::terraform-state-soat16/techchallenge-oficina/fase-4/*`) para o usuário `terraform`.
2. Usuário IAM `terraform`, cujas access keys vão para os secrets do GitHub. Precisa de permissões para criar a foundation (VPC, IAM, EKS, API Gateway, NLB e o attachment dos target groups ao Auto Scaling Group; lista no [README da pasta](../README.md#3-pré-requisitos-manuais-uma-vez)), consultar o usuário `cluster_admin`, criar Access Entries no EKS e, no apply dos manifests, executar `eks:DescribeCluster`, `rds:DescribeDBInstances` e `ssm:GetParameter` (chave do usuário IAM da Management, que acessa o SQS e o DynamoDB).
3. Usuário IAM `cluster_admin`. **Obrigatório**: a foundation o consulta por data source, e o `plan` falha se ele não existir.
4. Secrets, Variables e o Environment `destroy` no GitHub, descritos em [.github/workflows/README.md](../../.github/workflows/README.md).
5. Conta Datadog (trial de 14 dias), com a API Key e a Application Key nos secrets do GitHub, exceto com `DD_ENABLED=false` ([DATADOG.md](DATADOG.md#2-configuração)).

O Terraform não cria esses usuários, o bucket nem a conta do Datadog. Access keys, chaves do Datadog, secrets, `terraform.tfvars` real e `kubernetes/k8s/.env` nunca devem ser versionados.

## Arquitetura

```text
Internet
   |
   | HTTPS
   v
API Gateway (HTTP API techchallenge-oficina-api, stage $default)
   |  POST /auth/token (público) ------------------> Lambda techchallenge-oficina-issue-token (repo LAMBDA)
   |  GET /management/health, GET /os/health, GET /billing/health e GET /execution/health (públicos)
   |  POST /billing/api/pagamentos/notificacoes (público, webhook do Mercado Pago) -> /api/pagamentos/notificacoes do Billing
   |  ANY /management/{proxy+}, ANY /os/{proxy+}, ANY /billing/{proxy+} e ANY /execution/{proxy+} --> Lambda authorizer techchallenge-oficina-authorizer (repo LAMBDA)
   |  o prefixo sai do path (/management/api/x -> /api/x, /os/api/x -> /api/x, /billing/api/x -> /api/x, /execution/api/x -> /api/x)
   v
VPC Link (subnets privadas)
   |
   v
NLB interno (subnets privadas): um listener e um target group por API
   |
   +--> /management --> :30080 (techchallenge-oficina-management) --> NodePort 30080 --> management-api:80 --> container:8080
   +--> /os         --> :30081 (techchallenge-oficina-os)         --> NodePort 30081 --> os-service-api:80 --> container:8080 (repo OS-SERVICE)
   +--> /billing    --> :30082 (techchallenge-oficina-billing)    --> NodePort 30082 --> billing-api:80    --> container:8080 (repo BILLING)
   +--> /execution  --> :30083 (techchallenge-oficina-execution)  --> NodePort 30083 --> execution-api:80  --> container:8080 (repo EXECUTION)

VPC 10.0.0.0/16 (tag Project = techchallenge-oficina)
  Subnets públicas techchallenge-oficina-public-{1,2}   (10.0.0.0/20, 10.0.16.0/20)
    -> EKS e nodes; rota 0.0.0.0/0 pelo Internet Gateway
  Subnets privadas techchallenge-oficina-private-{1,2}  (10.0.128.0/20, 10.0.144.0/20)
    -> RDS (DB/infra), RDS do OS Service (repo OS-SERVICE), RDS do Billing (repo BILLING), RDS do Execução e Produção (repo EXECUTION),
       VPC Link e NLB interno; sem NAT e sem rota para a Internet
```

Não há NAT Gateway. Os nodes usam saída pelas subnets públicas para acessar ECR e demais serviços AWS, inclusive as filas SQS e as tabelas DynamoDB das quatro APIs (o DynamoDB fica fora da VPC, e não há VPC endpoint). Pela mesma saída, o Datadog Agent (namespace `datadog`) envia ao Datadog as métricas, os logs e os traces. No sentido inverso, os synthetics do Datadog chamam `GET /management/health`, `GET /os/health`, `GET /billing/health` e `GET /execution/health` pelo API Gateway, como qualquer cliente externo.

### Security Groups

| Security Group | Regras |
|---|---|
| `techchallenge-oficina-cluster-sg` | SG adicional do control plane; sem entrada, saída liberada |
| `techchallenge-oficina-nodes-sg` | Entrada total entre os próprios nodes e a partir do SG do cluster; entrada TCP `30000-32767` (NodePorts) só a partir da VPC (`10.0.0.0/16`), para o NLB; saída liberada |
| `techchallenge-oficina-vpc-link-sg` | SG do VPC Link do API Gateway; sem entrada, saída liberada |

O launch template anexa aos nodes o SG do cluster, o `techchallenge-oficina-nodes-sg` e o SG gerenciado pelo EKS. Os SGs dos bancos não são criados aqui. O `DB/infra` cria o próprio SG (`techchallenge-oficina-rds-sg`), com entrada `5432` a partir do `techchallenge-oficina-nodes-sg`, e o repo LAMBDA adiciona a própria regra nesse SG. Os repos OS-SERVICE, BILLING e EXECUTION criam o SG do banco de cada um (`techchallenge-oficina-os-rds-sg`, `techchallenge-oficina-billing-rds-sg` e `techchallenge-oficina-execution-rds-sg`), também com entrada `5432` a partir do SG dos nodes.

As regras garantem:

- Nenhuma entrada pública nas portas `8080` ou dos Services: os NodePorts só aceitam tráfego de dentro da VPC. O NLB não tem SG e preserva o IP de origem, que é o do VPC Link ou o do health check do NLB, ambos dentro da VPC.
- Nenhum SSH aberto para a Internet.
- A única entrada pública é o API Gateway; o NLB é interno.

## Foundation

Localização: `kubernetes/infra/foundation/`

Responsabilidades:

- VPC `10.0.0.0/16` com DNS habilitado.
- Duas subnets públicas para EKS e nodes.
- Duas subnets privadas, em duas AZs, para o DB subnet group do RDS (`DB/infra`) e para o VPC Link e o NLB interno. A route table privada não tem rotas além da local.
- Internet Gateway e rotas públicas.
- Security Groups do cluster e dos nodes.
- IAM roles do EKS e dos nodes, com `AmazonEC2ContainerRegistryReadOnly` na role dos nodes.
- EKS `techchallenge-oficina-eks`, versão `1.36`, `authentication_mode = "API"`, endpoint público e privado.
- Managed node group:
  - Instância `t3.small`, elegível ao Free Tier nesta conta.
  - `min_size = 2`.
  - `desired_size = 2`.
  - `max_size = 2`.
  - Disco EBS `gp3` de `30 GiB`, criptografado, e IMDSv2 obrigatório no launch template.
- Access Entry com `AmazonEKSClusterAdminPolicy` para o usuário `cluster_admin` (obrigatório).
- Access Entry com `AmazonEKSClusterAdminPolicy` para quem executa o Terraform (`aws_caller_identity`).
- Acesso externo às APIs ([api-gateway.tf](foundation/api-gateway.tf) e [nlb.tf](foundation/nlb.tf)), com um conjunto de rota, integração, listener e target group por entrada de `local.api_node_ports`:
  - API Gateway HTTP API `techchallenge-oficina-api`, com stage `$default` (auto deploy) e as rotas `ANY /management/{proxy+}`, `ANY /os/{proxy+}`, `ANY /billing/{proxy+}` e `ANY /execution/{proxy+}`. A integração remove o prefixo do path (`overwrite:path`), como fazia o `rewrite-target` do antigo Ingress.
  - Lambda authorizer (`REQUEST`, payload `2.0`, respostas simples, sem cache) nas rotas `ANY /<api>/{proxy+}`. Sem header `Authorization`, o gateway responde 401; token negado pelo authorizer responde 403.
  - Rotas públicas: `GET /management/health`, `GET /os/health`, `GET /billing/health` e `GET /execution/health`, cada uma com integração própria que reescreve o path para `/health`; `POST /billing/api/pagamentos/notificacoes`, o webhook do Mercado Pago, com integração própria que reescreve o path para `/api/pagamentos/notificacoes` (a rota exata tem prioridade sobre `/billing/{proxy+}`); e `POST /auth/token`, integrada (`AWS_PROXY`) à Lambda de geração do JWT.
  - As Lambdas são referenciadas pelo nome fixo (seção [Contrato com o repo LAMBDA](#contrato-com-o-repo-lambda)), então o apply não depende delas já existirem. Até o repo LAMBDA publicá-las, as rotas protegidas das APIs e o `/auth/token` respondem 500.
  - VPC Link `techchallenge-oficina-vpc-link` nas subnets privadas.
  - NLB interno `techchallenge-oficina-nlb`, com cross-zone. Tem um listener TCP e um target group por API, na porta do NodePort: `techchallenge-oficina-management` (30080), `techchallenge-oficina-os` (30081), `techchallenge-oficina-billing` (30082) e `techchallenge-oficina-execution` (30083). Os target groups são do tipo `instance`, com health check HTTP em `/health`. O `techchallenge-oficina-os`, o `techchallenge-oficina-billing` e o `techchallenge-oficina-execution` só ficam `healthy` depois do Bootstrap dos repos OS-SERVICE, BILLING e EXECUTION.
  - Os target groups são anexados ao Auto Scaling Group do node group (`aws_autoscaling_attachment`), então nodes novos entram sozinhos.
  - As portas ficam em `local.api_node_ports` (`management = 30080`, `os = 30081`, `billing = 30082` e `execution = 30083`, em [locals.tf](foundation/locals.tf)), e a chave vira o prefixo da rota e o sufixo do target group. Cada porta precisa ser igual ao `nodePort` do Service da API: `kubernetes/k8s/features/management-api/service.yml`, neste monorepo, `kubernetes/k8s/features/os-service-api/service.yml`, no repo OS-SERVICE, `kubernetes/k8s/features/billing-api/service.yml`, no repo BILLING, e `kubernetes/k8s/features/execution-api/service.yml`, no repo EXECUTION.

O usuário `terraform` é o acesso principal: executa foundation, addons e `kubectl`, no CI e localmente. Rode sempre com esse mesmo usuário. Com outra identidade, a Access Entry do executor anterior é substituída, e o CI pode perder o acesso ao cluster.

Outputs: `cluster_name`, `cluster_endpoint`, `cluster_region`, `api_gateway_endpoint` (URL pública da API) e `billing_webhook_url` (URL do webhook do Mercado Pago).

## Addons

Localização: `kubernetes/infra/addons/`

Responsabilidades:

- Usar as mesmas credenciais do usuário `terraform`.
- Consultar o cluster pelo nome, sem ler o state remoto da foundation. Por isso, o `plan` do addons exige o cluster EKS de pé e, com o Datadog ligado, chaves do Datadog válidas.
- Instalar Metrics Server (chart `3.12.2`) via Helm, usado pelo HPA.
- Instalar o Datadog Agent (chart `3.255.0`, namespace `datadog`) via Helm, em [datadog-agent.tf](addons/datadog-agent.tf) com os valores de [datadog-agent-values.yaml](addons/datadog-agent-values.yaml): um Agent por node e um Cluster Agent, APM .NET por Single Step Instrumentation nos namespaces `oficina`, `os-service`, `billing` e `execution`, coleta dos logs dos containers e `http_check` no `/health/ready` das quatro APIs.
- Criar os recursos do Datadog no módulo local [addons/modules/datadog/](addons/modules/datadog), chamado por [datadog.tf](addons/datadog.tf): 5 métricas de log, 10 monitores, 4 synthetics (`GET /management/health`, `GET /os/health`, `GET /billing/health` e `GET /execution/health`) com 1 SLO e 1 dashboard (output `datadog_dashboard_url`). Os synthetics leem a URL do API Gateway por data source.
- O Agent e o módulo têm `count` por `datadog_enabled`. Com `false` (`DD_ENABLED=false` no CI), os dois saem no próximo apply.

No CI, as chaves e as opções do Datadog chegam por `TF_VAR_datadog_*`, a partir dos secrets e variables do GitHub. Localmente, as chaves vão por `TF_VAR_datadog_*`, e as opções ficam no `terraform.tfvars` ([README.md](README.md#4-instale-os-addons)). Detalhes em [DATADOG.md](DATADOG.md).

## Contrato com o DB (`DB/infra`)

O [`DB/infra`](../../DB/README.md) encontra a rede pelo cluster EKS e por nome ([data.tf](../../DB/infra/data.tf)), sem ler o state da foundation. Os `DB/infra` dos repos OS-SERVICE, BILLING e EXECUTION fazem o mesmo. Estes nomes **não podem mudar**:

| O que | Valor |
|---|---|
| Cluster EKS | `techchallenge-oficina-eks`. A VPC é lida do cluster (`data "aws_eks_cluster"` → `vpc_config`) |
| Subnets privadas | `Name = techchallenge-oficina-private-1` e `techchallenge-oficina-private-2`, em AZs diferentes. O `DB/infra` filtra `techchallenge-oficina-private-*` na VPC do cluster |
| SG dos nodes | `techchallenge-oficina-nodes-sg`, anexado aos nodes pelo launch template |

Alterar `project_name`, `cluster_name` ou o nome/descrição do SG dos nodes recria esses recursos e quebra os quatro `DB/infra`. No sentido inverso, o apply dos manifests consome do DB o identifier do RDS `techchallenge-oficina-postgres`, para descobrir o endpoint.

## Contrato com o repo OS-SERVICE

O [repo OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658) não aplica nada desta pasta: ele usa o que a foundation e o addons criam e cria só o próprio RDS e os próprios manifests.

| O que | Valor | Uso |
|---|---|---|
| Rota e NodePort | Prefixo `os` e porta `30081` em `local.api_node_ports`; target group `techchallenge-oficina-os` | O Service `os-service-api` do OS-SERVICE usa `nodePort: 30081` |
| Namespace e serviço | Namespace `os-service`, Deployment e Service `os-service-api` | APM (Single Step Instrumentation), `http_check` em `http://os-service-api.os-service.svc.cluster.local/health/ready` e synthetic em `GET /os/health` |
| Rede | Cluster `techchallenge-oficina-eks`, subnets `techchallenge-oficina-private-*` e SG `techchallenge-oficina-nodes-sg` | O `DB/infra` do OS-SERVICE cria o RDS `techchallenge-oficina-os-postgres` e o SG `techchallenge-oficina-os-rds-sg` nesta rede |
| Destroy | RDS `techchallenge-oficina-os-postgres` e SG `techchallenge-oficina-os-rds-sg` | O Destroy deste monorepo falha (`os-service-check`) se eles ainda existirem |

As filas e a chave de acesso do OS Service no SSM vêm do [SQS/](../../SQS/README.md) deste monorepo, não desta pasta.

## Contrato com o repo BILLING

O [repo BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658) também não aplica nada desta pasta: usa o que a foundation e o addons criam e cria só o próprio RDS e os próprios manifests.

| O que | Valor | Uso |
|---|---|---|
| Rota e NodePort | Prefixo `billing` e porta `30082` em `local.api_node_ports`; target group `techchallenge-oficina-billing` | O Service `billing-api` do BILLING usa `nodePort: 30082` |
| Webhook do Mercado Pago | Rota pública `POST /billing/api/pagamentos/notificacoes` e output `billing_webhook_url` | O `_k8s-apply.yml` do BILLING monta a mesma URL, a partir do endpoint do API Gateway, e a grava no Secret do Billing |
| Namespace e serviço | Namespace `billing`, Deployment e Service `billing-api` | APM (Single Step Instrumentation), `http_check` em `http://billing-api.billing.svc.cluster.local/health/ready` e synthetic em `GET /billing/health` |
| Rede | Cluster `techchallenge-oficina-eks`, subnets `techchallenge-oficina-private-*` e SG `techchallenge-oficina-nodes-sg` | O `DB/infra` do BILLING cria o RDS `techchallenge-oficina-billing-postgres` e o SG `techchallenge-oficina-billing-rds-sg` nesta rede |
| Destroy | RDS `techchallenge-oficina-billing-postgres` e SG `techchallenge-oficina-billing-rds-sg` | O Destroy deste monorepo falha (`billing-check`) se eles ainda existirem |

As filas e a chave de acesso do Billing no SSM também vêm do [SQS/](../../SQS/README.md) deste monorepo.

## Contrato com o repo EXECUTION

O [repo EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658) (Execução e Produção) também não aplica nada desta pasta: usa o que a foundation e o addons criam e cria só o próprio RDS e os próprios manifests.

| O que | Valor | Uso |
|---|---|---|
| Rota e NodePort | Prefixo `execution` e porta `30083` em `local.api_node_ports`; target group `techchallenge-oficina-execution`. Rotas `ANY /execution/{proxy+}` (com o authorizer) e `GET /execution/health` (pública) | O Service `execution-api` do EXECUTION usa `nodePort: 30083` |
| Namespace e serviço | Namespace `execution`, Deployment e Service `execution-api` | APM (Single Step Instrumentation), `http_check` em `http://execution-api.execution.svc.cluster.local/health/ready` e synthetic em `GET /execution/health` |
| Rede | Cluster `techchallenge-oficina-eks`, subnets `techchallenge-oficina-private-*` e SG `techchallenge-oficina-nodes-sg` | O `DB/infra` do EXECUTION cria o RDS `techchallenge-oficina-execution-postgres` e o SG `techchallenge-oficina-execution-rds-sg` nesta rede |
| Destroy | RDS `techchallenge-oficina-execution-postgres` e SG `techchallenge-oficina-execution-rds-sg` | O Destroy deste monorepo falha (`execution-check`) se eles ainda existirem |

As filas e a chave de acesso do Execução e Produção no SSM (`/techchallenge-oficina/sqs/execution/...`, usuário IAM `techchallenge-oficina-execution-sqs`) também vêm do [SQS/](../../SQS/README.md) deste monorepo. Ele consome a fila `techchallenge-oficina-execution.fifo` e publica só na do OS Service.

## Contrato com o repo LAMBDA

O [repo LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658) publica as funções com AWS SAM e cria a permissão para o API Gateway invocá-las. A foundation as referencia pelo nome, sem ler o state do LAMBDA ([locals.tf](foundation/locals.tf)):

| O que | Valor | Uso aqui |
|---|---|---|
| Lambda authorizer | `techchallenge-oficina-authorizer` | `aws_apigatewayv2_authorizer.lambda`, aplicado às rotas `ANY /<api>/{proxy+}` (`management`, `os`, `billing` e `execution`) |
| Lambda de geração do JWT | `techchallenge-oficina-issue-token` | Rota `POST /auth/token` |
| Rota liberada sem `cpf` | `POST /management/api/clientes` | O authorizer aceita JWT sem `cpf` nessa rota. Mudar o prefixo `management` em `local.api_node_ports` exige mudar o LAMBDA. As rotas `/os/...`, `/billing/...` e `/execution/...` exigem sempre o `cpf` (o webhook do Mercado Pago não passa pelo authorizer) |
| SG da Lambda na VPC | `techchallenge-oficina-lambda-sg` | O Destroy confere que ele já foi removido |

A Lambda de busca de clientes do repo LAMBDA fica nas subnets privadas, com o próprio SG e uma regra no SG do RDS. Por isso o LAMBDA é destruído antes deste monorepo (RDS e rede).

## Aplicação dos manifests

Os manifests são aplicados com Kustomize ([k8s/kustomization.yaml](../k8s/kustomization.yaml)), nunca com `kubectl apply -f` por pasta: o Secret `oficina-api-secrets` é gerado pelo `secretGenerator` a partir de `k8s/.env`, que não é versionado. O modelo é [k8s/.env.example](../k8s/.env.example).

Ordem no deploy, feita pelos jobs do Bootstrap (e na mesma ordem pelo Deploy):

1. Foundation → addons (`kubernetes/infra/`).
2. RDS e tabela DynamoDB do histórico de movimentações de estoque (`DB/infra`).
3. Imagem da Management API no ECR (`server/`).
4. Manifests (`kubernetes/k8s/`), com rollout restart.

As filas (`SQS/infra`, com os usuários IAM e a política da tabela DynamoDB da Management) são aplicadas em paralelo, antes dos manifests. Depois, os Bootstraps dos repos OS-SERVICE e EXECUTION, nesta ordem, e o do BILLING (a qualquer momento), e por fim o Deploy do repo LAMBDA.

O apply dos manifests ([_k8s-apply.yml](../../.github/workflows/_k8s-apply.yml), chamado pelo Bootstrap, pelo Deploy e pelo workflow manual [K8s Apply](../../.github/workflows/k8s-apply.yml), este com `restart-pods` padrão `true`) descobre o endpoint do RDS, monta o `kubernetes/k8s/.env` com a connection string e o `ResendSettings__ApiKey` a partir dos secrets do GitHub e com a chave do usuário IAM da Management (SQS e DynamoDB) lida do SSM, roda `kubectl apply -k kubernetes/k8s` e, se pedido, faz o `rollout restart` da API. O restart é necessário quando o Secret muda (`disableNameSuffixHash` mantém o mesmo nome) ou quando há imagem nova, já que o Deployment usa a tag `latest` com `imagePullPolicy: Always`. O Deploy aplica os manifests, com restart, em todo push na `main` que altere alguma parte (aplicação, `DB/infra`, `SQS/infra`, `kubernetes/infra` ou `kubernetes/k8s`).

O Service da Management API é `NodePort`, com porta fixa (30080) que o NLB da foundation usa como destino; o do OS Service usa a 30081, o do Billing, a 30082, e o do Execução e Produção, a 30083. Não há manifest de Ingress.

## Validação

Sem acessar a AWS, em `kubernetes/infra/foundation/` e em `kubernetes/infra/addons/`:

```powershell
terraform init -backend=false
terraform fmt -check -recursive
terraform validate
```

Manifests, a partir da raiz do monorepo (o `.env` gerado só tem placeholders e não deve ser aplicado):

```powershell
Copy-Item kubernetes\k8s\.env.example kubernetes\k8s\.env
kubectl kustomize kubernetes/k8s
```

São as mesmas verificações obrigatórias dos PRs (`validate-foundation`, `validate-addons` e `validate-k8s`). O `plan` com AWS roda no PR como informativo. O `apply` não é manual: roda pelo Bootstrap ou pelo push na `main`, primeiro na foundation e depois nos addons.

O CI não usa `terraform.tfvars`: aplica os defaults de `variables.tf` e, no addons, recebe as chaves e as opções do Datadog como `TF_VAR_datadog_*`, a partir dos secrets e variables do GitHub. Localmente, as chaves vão por `TF_VAR_datadog_*` ([README.md](README.md#4-instale-os-addons)). Se criar um `terraform.tfvars` a partir do `terraform.tfvars.example`, mantenha os mesmos valores do CI, inclusive `datadog_enabled`, `datadog_site` e `datadog_synthetics_status` iguais às variables `DD_*`, pois o state é o mesmo. Ao adicionar um provider (como o `DataDog/datadog`, no addons), rode `terraform init` e depois `terraform providers lock -platform=linux_amd64 -platform=windows_amd64`; para atualizar os providers, use `terraform init -upgrade` antes do `providers lock`.

## Destruição e custos

EKS (control plane cobrado por hora), os dois nodes com seus discos EBS, o NLB interno (cobrado por hora) e o API Gateway (cobrado por requisição) geram custos enquanto existem. O Datadog não tem custo durante o trial de 14 dias, sem cartão; antes do fim, defina `DD_ENABLED=false` e rode o Bootstrap, ou rode o Destroy ([DATADOG.md](DATADOG.md#4-trial-de-14-dias)).

Ordem: **LAMBDA**, **OS-SERVICE**, **BILLING** e **EXECUTION** (repos separados, em qualquer ordem) → **Destroy** deste monorepo, que apaga o RDS antes da rede. Os ECRs são manuais e ficam fora do destroy. Os RDS usam as subnets privadas e os SGs deles referenciam o SG dos nodes; se algum ainda existir, o destroy da VPC falha com `DependencyViolation`.

Rode o workflow [Destroy](../../.github/workflows/destroy.yml) com `confirm = destroy` e aprove no Environment `destroy`. A sequência é:
1. `lambda-check`: falha se o SG `techchallenge-oficina-lambda-sg` (repo LAMBDA) ainda existir.
2. `os-service-check`: falha se o RDS `techchallenge-oficina-os-postgres` ou o SG `techchallenge-oficina-os-rds-sg` (repo OS-SERVICE) ainda existirem.
3. `billing-check`: falha se o RDS `techchallenge-oficina-billing-postgres` ou o SG `techchallenge-oficina-billing-rds-sg` (repo BILLING) ainda existirem.
4. `execution-check`: falha se o RDS `techchallenge-oficina-execution-postgres` ou o SG `techchallenge-oficina-execution-rds-sg` (repo EXECUTION) ainda existirem.
5. `gate`: aprovação manual, depois dos quatro checks.
6. SQS (`SQS/infra`), em paralelo com o RDS: filas, usuários IAM (com as políticas inline, inclusive a da tabela DynamoDB da Management) e parâmetros do SSM.
7. RDS e tabela DynamoDB (`DB/infra`): os dados e o histórico de movimentações de estoque são perdidos.
8. Addons: Metrics Server, Datadog Agent e recursos do Datadog.
9. Foundation, que leva junto o API Gateway, o VPC Link e o NLB. Nenhum Load Balancer é criado pelo Kubernetes, então não há o que limpar antes. Roda mesmo se o destroy dos addons falhar (por exemplo, com chaves do Datadog revogadas), para o EKS não ficar ligado gerando custo; o run fica vermelho. Não roda se o destroy do RDS falhar ou se o dos addons for cancelado. O bucket S3 do state e os ECRs ficam fora do `destroy`.
