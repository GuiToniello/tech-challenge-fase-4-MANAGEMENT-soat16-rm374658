# Pipelines (GitHub Actions)

As pipelines deste monorepo cuidam de tudo que ficava em três repositórios na fase 3, mais as filas da fase 4:

- **Aplicação** (`server/`): build e testes, além da imagem da Management API no ECR, que é criado manualmente.
- **Cluster e borda** (`kubernetes/`): VPC, EKS, API Gateway (com as rotas `/management` e `/os`), NLB, addons e manifests.
- **Banco** (`DB/`): o RDS PostgreSQL da Management.
- **Filas** (`SQS/`): as filas Amazon SQS FIFO entre a Management e o OS Service, os usuários IAM e as chaves no SSM Parameter Store.

A ordem entre as partes, que antes era manual entre repositórios, agora é feita com `needs` entre jobs.

```
.github/workflows/
  _build-test.yml   (workflow_call)      - restore, build e testes da solution .slnx
  _build-push.yml   (workflow_call)      - docker compose build + push da imagem da Management API no ECR
  _terraform.yml    (workflow_call)      - validate/plan/apply/destroy de uma pasta do Terraform
  _k8s-apply.yml    (workflow_call)      - kustomize offline (validate) ou kubectl apply -k no EKS (apply)
  bootstrap.yml     (workflow_dispatch)  - sobe o ambiente inteiro, na ordem
  deploy.yml        (pull_request, push) - PR: valida tudo; push na main: aplica só o que mudou, na ordem
  destroy.yml       (workflow_dispatch)  - derruba o ambiente inteiro, na ordem inversa
  k8s-apply.yml     (workflow_dispatch)  - reaplica os manifests e reinicia a API
```

> Convenção: workflows que começam com `_` são internos e são chamados com `uses: ./.github/workflows/_x.yml`.

**Ordem com os repositórios OS-SERVICE e LAMBDA**, que são separados:
- **Para subir:** Bootstrap deste repo → Bootstrap do [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658) → Deploy do [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O OS-SERVICE usa a VPC, o EKS, a rota `/os` e as filas criados aqui; as Lambdas usam a VPC e o RDS.
- **Para derrubar:** Destroy do LAMBDA e Destroy do OS-SERVICE → Destroy deste repo.

## 1. Workflows reutilizáveis

### `_build-test.yml`
- `actions/checkout@v7` e `actions/setup-dotnet@v6` (10.0.x).
- `dotnet restore`, `dotnet build -c Release` e `dotnet test -c Release` em `server/TechChallenge.Oficina.API.slnx`.
- Os testes usam banco em memória e mocks (inclusive do SQS). Não precisam de AWS, banco nem Docker, então rodam também em PR de fork.

### `_build-push.yml`
- Recebe o input `image-tag`. O Bootstrap e o Deploy passam sempre `latest`, a tag fixa dos manifests em `kubernetes/k8s/`.
- Passos:
  1. Bloqueio fora da `main`: falha com erro em vez de publicar código não revisado.
  2. Confere se a variable `ECR_REGISTRY` existe.
  3. Configura as credenciais AWS e faz login no ECR.
  4. Roda `docker compose build` e `docker compose push` do serviço `management-api`, com `REGISTRY_PREFIX=<ECR_REGISTRY>/` e `IMAGE_TAG=<image-tag>`.
- Concorrência: na `main` usa o grupo fixo `ecr-push`, que serializa os pushes.

### `_terraform.yml`
- Inputs:
  - `working-directory`: `kubernetes/infra/foundation`, `kubernetes/infra/addons`, `DB/infra` ou `SQS/infra`.
  - `command`: `validate`, `plan`, `apply` ou `destroy`.
- Passos:
  1. Bloqueio de apply/destroy fora da `main`.
  2. Só no addons: confere os secrets e variables do Datadog. Pule essa conferência com `DD_ENABLED=false`.
  3. Configura as credenciais AWS (exceto no validate).
  4. Terraform 1.15.8.
  5. `init`, com `-backend=false` no validate.
  6. `fmt -check -recursive` e `validate`.
  7. `plan -lock=false`, ou então `apply`/`destroy -auto-approve -lock-timeout=15m`.
- **Variáveis:** todas as pastas recebem os mesmos `TF_VAR_*`:
  - `TF_VAR_rds_password`, do secret `RDS_PASSWORD`;
  - os `TF_VAR_datadog_*`, dos secrets e variables `DD_*`.

  O Terraform ignora `TF_VAR_` de variável que a pasta não declara. O `SQS/infra` não usa nenhum deles.
- **Concorrência:** um grupo por pasta. Validate e plan cancelam o run anterior; apply e destroy esperam na fila.

| Pasta | State (bucket `terraform-state-soat16`) |
|---|---|
| `kubernetes/infra/foundation` | `techchallenge-oficina/fase-4/k8s-foundation.tfstate` |
| `kubernetes/infra/addons` | `techchallenge-oficina/fase-4/k8s-addons.tfstate` |
| `DB/infra` | `techchallenge-oficina/fase-4/database.tfstate` |
| `SQS/infra` | `techchallenge-oficina/fase-4/sqs.tfstate` |

### `_k8s-apply.yml`
- Inputs: `command` (`validate` ou `apply`) e `restart-pods` (boolean).
- **validate:** copia `kubernetes/k8s/.env.example` para `.env` e roda `kubectl kustomize kubernetes/k8s`, offline.
- **apply:**
  1. Bloqueio fora da `main`.
  2. Checkout da **ponta da `main`**, para um run antigo não sobrescrever manifests mais novos.
  3. Confere os secrets e variables.
  4. Roda `aws eks update-kubeconfig`.
  5. Descobre o endpoint do RDS pelo `RDS_INSTANCE_IDENTIFIER`.
  6. Gera `kubernetes/k8s/.env`, com a connection string e a chave do Resend.
  7. Lê do SSM a chave do usuário IAM da Management no SQS (`/techchallenge-oficina/sqs/management/access-key-id` e `/secret-access-key`), mascara os valores no log e os acrescenta ao `.env` como `AWS_ACCESS_KEY_ID` e `AWS_SECRET_ACCESS_KEY`. Se os parâmetros não existirem, falha pedindo o Bootstrap.
  8. `kubectl apply -k kubernetes/k8s`.
  9. Se `restart-pods`, `kubectl rollout restart`.
- O Secret `oficina-api-secrets` não tem hash no nome. Por isso, trocar a senha, a chave do Resend ou a chave do SQS só chega aos pods com restart.

## 2. `bootstrap.yml`

- **Gatilho:** `workflow_dispatch`, a partir da `main`.
- **Jobs, nesta ordem:**
  1. `build-test`, para falhar antes de criar recursos pagos;
  2. `terraform-foundation` e, em paralelo, `terraform-sqs` (as filas não dependem da VPC nem do EKS);
  3. `terraform-addons`, depois da foundation;
  4. `terraform-db`;
  5. `build-push`;
  6. `k8s-apply` (com restart), depois do `build-push` e do `terraform-sqs`.

Use na primeira criação, para recriar o ambiente depois de um Destroy ou para reaplicar a HEAD da `main`. É também o jeito de aplicar uma troca das variables `DD_ENABLED` ou `DD_SYNTHETICS_STATUS`. Depois dele, rode o Bootstrap do repo OS-SERVICE e o Deploy do repo LAMBDA.

## 3. `deploy.yml`

| Evento | Jobs | Observação |
|---|---|---|
| `pull_request` → `main` (sem filtro de paths) | `build-test`, `validate-foundation`, `validate-addons`, `validate-db`, `validate-sqs`, `validate-k8s` | **Checks obrigatórios.** Não usam AWS |
| | `plan-foundation`, `plan-addons`, `plan-db`, `plan-sqs` | Informativos. O addons e o DB precisam do ambiente de pé (leem o cluster); o SQS, não. Pulados em PR de fork |
| `push` na `main` | `build-test` e `changes` (`dorny/paths-filter`) | Os filtros são `app` (`server/**`, `docker-compose.yml`), `infra` (`kubernetes/infra/**`), `db` (`DB/infra/**`), `sqs` (`SQS/infra/**`) e `k8s` (`kubernetes/k8s/**`). Cada filtro inclui também o workflow reutilizável correspondente |
| | `apply-foundation` → `apply-addons` | Se `infra` mudou |
| | `apply-db` | Se `db` mudou. Espera a infra |
| | `apply-sqs` | Se `sqs` mudou. Roda em paralelo, sem esperar a infra |
| | `build-push` | Se `app` mudou. Espera os testes e o DB |
| | `apply-k8s` (com restart) | Se qualquer parte mudou: imagem nova, manifests, RDS recriado, cluster recriado ou chave nova do SQS. Espera o `build-push` e o `apply-sqs` |

- Os jobs encadeados usam `!failure() && !cancelled()`: um job pulado não trava os seguintes, mas uma falha trava.
- **Concorrência:** os pushes na `main` rodam um de cada vez, na ordem (`deploy-main`). Em PR, um push novo cancela a verificação anterior.
- **O PR não usa filtro de paths de propósito.** Com filtro, os checks obrigatórios ficariam *pending* em PRs que só mexem em documentação.
- **Custo:**
  - **o primeiro push na `main`** marca todos os arquivos como alterados e aplica tudo. Se os secrets da AWS já estiverem configurados, ele cria o ambiente inteiro; sem eles, os jobs de apply falham na credencial e não criam nada;
  - um push em `kubernetes/infra/**` com o ambiente desligado recria o cluster do zero;
  - um push em `server/**` com o ambiente desligado publica a imagem, mas o `apply-k8s` falha porque não há cluster;
  - um push em `SQS/infra/**` com o ambiente desligado recria as filas, os usuários e as chaves (sem custo relevante), e o `apply-k8s` falha porque não há cluster.
- **Chave do SQS recriada:** o `apply-k8s` daqui atualiza só o Secret da Management. Rode o K8s Apply do repo OS-SERVICE para o OS Service receber a chave nova.
- **Não use "Re-run" em um Deploy antigo:** ele reaplicaria a infra e publicaria `latest` de um commit antigo. Para reaplicar a HEAD, use o Bootstrap.
- **Mudanças só no `deploy.yml`** não aplicam nada. Se precisar, rode o Bootstrap ou o K8s Apply.

## 4. `destroy.yml`

- **Gatilho:** `workflow_dispatch` com o input obrigatório `confirm`. O GitHub não diferencia maiúsculas nessa comparação.

| Job | Quando roda | O que faz |
|---|---|---|
| `reject` | `confirm` ≠ `destroy` ou branch ≠ `main` | **Falha** com erro, em vez de terminar verde sem ter destruído nada |
| `lambda-check` | `confirm` = `destroy` na `main` | **Falha** se o SG `techchallenge-oficina-lambda-sg` (Lambda do repo LAMBDA na VPC) ainda existir. Rode antes o Destroy do LAMBDA |
| `os-service-check` | `confirm` = `destroy` na `main` | **Falha** se o RDS `techchallenge-oficina-os-postgres` ou o SG `techchallenge-oficina-os-rds-sg` (repo OS-SERVICE) ainda existirem: o RDS fica nas subnets privadas, e o SG referencia o SG dos nodes. Rode antes o Destroy do OS-SERVICE |
| `gate` | depois do `lambda-check` e do `os-service-check` | `environment: destroy`: aprovação manual. É um job à parte porque um job com `uses:` não aceita `environment` |
| `terraform-destroy-sqs` | depois do `gate` | Destroy do `SQS/infra`: filas, DLQs, usuários IAM e parâmetros do SSM. Roda em paralelo com o do DB |
| `terraform-destroy-db` | depois do `gate` | Destroy do `DB/infra`. **Os dados do RDS são perdidos** |
| `terraform-destroy-addons` | depois do DB | Destroy do `kubernetes/infra/addons`: Metrics Server, Datadog Agent e recursos do Datadog |
| `terraform-destroy-foundation` | depois do DB e dos addons | Destroy do `kubernetes/infra/foundation`: EKS, API Gateway, VPC Link, NLB e VPC |

- **A foundation roda mesmo se os addons falharem**, por exemplo com chaves do Datadog revogadas, para o EKS nunca ficar ligado gerando custo. O run continua vermelho.
- **A foundation não roda** se o destroy do DB falhar (a VPC não sairia com o RDS dentro) ou se o dos addons for cancelado. Nesses casos, rode o Destroy de novo.
- Configure o environment `destroy` **antes** do primeiro uso, com *Required reviewers* e *Deployment branches* restrito a `main`. Se ele não existir, o GitHub o cria sem proteção.
- **Não dispare o Destroy com Deploy, Bootstrap ou K8s Apply em andamento.** Os jobs dividem os grupos de concorrência.

## 5. `k8s-apply.yml`

- **Gatilho:** `workflow_dispatch`, com o input `restart-pods` (padrão `true`).
- **Quando usar:**
  - depois de trocar `RESEND_API_KEY`;
  - para reiniciar a API sem passar pelo Deploy.
- **Para trocar `RDS_PASSWORD`, use o Bootstrap, não o K8s Apply.** A senha nova precisa ser aplicada no RDS (`DB/infra`) antes de ir para o Secret. Depois rode o Deploy do repo LAMBDA.
- Mudanças no spec do Deployment (imagem, recursos, probes) já fazem rollout sozinhas.

---

## Secrets e Variables

Configure em **Settings → Secrets and variables → Actions**:

| Nome | Tipo | Uso |
|---|---|---|
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Secret | Todos os jobs que acessam a AWS (usuário IAM `terraform`) |
| `RDS_PASSWORD` | Secret | `_terraform.yml` (`TF_VAR_rds_password` do `DB/infra`) e `_k8s-apply.yml` (connection string). O repo LAMBDA usa o mesmo valor |
| `RESEND_API_KEY` | Secret | `_k8s-apply.yml` |
| `DD_API_KEY` / `DD_APP_KEY` | Secret | `_terraform.yml`: conferência e `TF_VAR_datadog_*` do addons |
| `DD_ALERT_RECIPIENTS` | Secret (opcional) | Destinatários dos monitores (ex.: `@seu-email@dominio.com`). É secret porque o repositório é público e o plan imprime as mensagens |
| `AWS_REGION` | Variable | Todos (`us-east-1`) |
| `ECR_REGISTRY` | Variable | `_build-push.yml` (`903936907231.dkr.ecr.us-east-1.amazonaws.com`) |
| `EKS_CLUSTER_NAME` | Variable | `_k8s-apply.yml` (`techchallenge-oficina-eks`) |
| `RDS_INSTANCE_IDENTIFIER` | Variable | `_k8s-apply.yml` (`techchallenge-oficina-postgres`) |
| `RDS_DATABASE` / `RDS_USERNAME` | Variable | `_k8s-apply.yml` (`oficina` / `sa`) |
| `DD_SITE` | Variable | `_terraform.yml` (`datadoghq.com`) |
| `DD_ENABLED` | Variable (opcional) | Padrão `true`; `false` desliga o Datadog |
| `DD_SYNTHETICS_STATUS` | Variable (opcional) | Padrão `live`; `paused` pausa os synthetics |

Em relação à fase 3, saem o secret `K8S_REPO_TOKEN` e a variable `K8S_REPO`. O disparo entre repositórios deixou de existir. O SQS não tem secret nem variable: as chaves dos usuários IAM ficam no SSM e são lidas pelo `_k8s-apply.yml`.

**Permissões IAM do usuário `terraform`**: as mesmas da fase 3, com duas mudanças:
- O acesso ao state no S3 agora é em `arn:aws:s3:::terraform-state-soat16/techchallenge-oficina/fase-4/*`, com `s3:GetObject`, `s3:PutObject` e `s3:DeleteObject`, mais `s3:ListBucket` no bucket. Os detalhes estão em [kubernetes/README.md](../../kubernetes/README.md) e [DB/README.md](../../DB/README.md).
- O `SQS/infra` cria filas SQS, usuários IAM com policy inline e access key, e parâmetros SecureString no SSM; o `_k8s-apply.yml` lê esses parâmetros (`ssm:GetParameter`). Detalhes em [SQS/README.md](../../SQS/README.md).

Para o push no ECR:
- `ecr:GetAuthorizationToken`, em `*`.
- Em `arn:aws:ecr:us-east-1:903936907231:repository/techchallenge-oficina-*`:
  - `ecr:BatchCheckLayerAvailability`, `ecr:InitiateLayerUpload`, `ecr:UploadLayerPart`, `ecr:CompleteLayerUpload`
  - `ecr:PutImage`, `ecr:BatchGetImage`

**Proteção da `main`** (Settings → Branches): exija Pull Request e os checks abaixo. Eles aparecem na lista depois que o primeiro PR roda.
- `build-test / build-test`
- `validate-foundation / terraform`
- `validate-addons / terraform`
- `validate-db / terraform`
- `validate-sqs / terraform`
- `validate-k8s / kubectl`

Todos os callers passam os secrets com `secrets: inherit`, e todos os workflows declaram `permissions: contents: read`.
