# RFC-001: Escolha da nuvem

> Uma RFC (Request for Comments) é uma proposta escrita: apresenta um problema, compara as opções e recomenda uma, para que a decisão seja discutida antes de ser adotada. As RFCs e ADRs são numeradas em sequência única entre este monorepo e o repositório [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice está no [docs/arquitetura.md](../arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

- **Status**: Aceita
- **Repositórios afetados**: todos. Este monorepo (`kubernetes/`: rede, Kubernetes e API Gateway; `DB/`: banco; aplicação: imagem no Amazon ECR) e o LAMBDA (funções)
- **Resultado**: AWS, na região `us-east-1` (seção 6)

## 1. Resumo

O sistema precisa de serviços prontos (gerenciados) para rodar containers, banco de dados, funções e uma porta de entrada com autenticação. AWS, Microsoft Azure e Google Cloud oferecem tudo isso. A proposta é usar a **AWS, na região `us-east-1`**, porque a fase 2 já rodava nela, a conta e os pré-requisitos já existem, e o API Gateway da AWS se liga às funções Lambda e à rede privada sem servidores extras.

## 2. Contexto

- O sistema tem uma API .NET em container (a Management API), um banco relacional e funções de autenticação.
- A fase 2 já rodava no Amazon EKS, com Terraform e GitHub Actions. Na fase 3, os mesmos recursos foram divididos em quatro repositórios (APP, K8S, DB e LAMBDA). Na fase 4, APP, K8S e DB voltaram a um monorepo, e o LAMBDA continua separado ([ADR-018](../adr/ADR-018-Monorepo-e-API-Unica-Management.md)).
- É um projeto de estudo: o ambiente sobe para a demonstração e é destruído depois, para não gerar custo.
- Toda a infraestrutura é criada por código (Terraform e AWS SAM), pelas pipelines do GitHub Actions.

## 3. O que a nuvem precisa oferecer

| # | Necessidade | Para quê |
|---|---|---|
| N1 | Kubernetes gerenciado | Rodar a API em containers |
| N2 | PostgreSQL gerenciado, numa rede privada | Guardar os dados ([RFC-002](RFC-002-Escolha-do-Banco-de-Dados.md)) |
| N3 | Funções serverless | Autenticar o cliente pelo CPF ([RFC-003](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md)) |
| N4 | API Gateway com HTTPS e autorizador customizado | Ter uma entrada única que confere o token antes de chegar à API |
| N5 | Repositório privado de imagens Docker | Guardar a imagem da API |
| N6 | Bom suporte no Terraform e no GitHub Actions | Criar e destruir tudo por código |
| N7 | Custo baixo e previsível | Projeto de estudo |

## 4. Opções avaliadas

### 4.1. Os serviços equivalentes em cada nuvem

| Necessidade | AWS | Microsoft Azure | Google Cloud |
|---|---|---|---|
| Kubernetes gerenciado | Amazon EKS | Azure Kubernetes Service (AKS) | Google Kubernetes Engine (GKE) |
| PostgreSQL gerenciado | Amazon RDS for PostgreSQL | Azure Database for PostgreSQL | Cloud SQL for PostgreSQL |
| Funções serverless | AWS Lambda | Azure Functions | Cloud Run functions |
| Entrada com autorização | Amazon API Gateway + Lambda authorizer | Azure API Management | API Gateway ou Apigee |
| Imagens Docker | Amazon ECR | Azure Container Registry | Artifact Registry |
| Estado do Terraform | Amazon S3 | Azure Blob Storage | Cloud Storage |

As três nuvens atendem às necessidades N1 a N6. A escolha depende, principalmente, da continuidade do projeto e do custo.

### 4.2. Prós e contras

**AWS**
- ✅ Continuidade: a fase 2 já rodava no Amazon EKS, então o Terraform, as pipelines e o conhecimento são reaproveitados.
- ✅ A conta e os pré-requisitos já existem: bucket S3 do state do Terraform, usuários IAM e repositórios ECR.
- ✅ O API Gateway chama um Lambda authorizer e alcança a rede privada (VPC Link e NLB interno) de forma nativa, com HTTPS no endereço padrão, sem domínio nem certificado próprios.
- ✅ Lambda e API Gateway são cobrados por uso, e os nós `t3.small` são elegíveis ao nível gratuito (Free Tier) nesta conta.
- ⚠ O control plane do EKS, os nós e o NLB são cobrados por hora enquanto existem.
- ⚠ API Gateway, Lambda e IAM são serviços próprios da AWS: trocar de nuvem exigiria refazê-los.

**Microsoft Azure**
- ✅ Oferece todos os serviços, e o AKS não cobra o control plane no plano gratuito.
- ✅ Boa integração com .NET.
- ❌ Exigiria reescrever o Terraform e as pipelines e criar a conta e os pré-requisitos do zero.
- ⚠ A regra de autorização ficaria em políticas do API Management, um modelo diferente do usado no projeto.

**Google Cloud**
- ✅ Oferece todos os serviços, e o GKE tem um crédito mensal que cobre a taxa de gerenciamento de um cluster.
- ❌ Exigiria reescrever o Terraform e as pipelines e criar a conta e os pré-requisitos do zero.
- ⚠ O API Gateway do Google valida o JWT por configuração, mas não chama uma função autorizadora como o Lambda authorizer. A regra do `cpf` teria de ficar em outro serviço.

## 5. Região: `us-east-1`

- É a região da AWS com a maior oferta de serviços e, em geral, os menores preços.
- Todos os recursos ficam na mesma região, sem tráfego (nem custo) entre regiões.
- Os serviços externos usados também ficam nos EUA: o tenant do Auth0 (`*.us.auth0.com`) e o site US1 do Datadog.
- Contra: para usuários no Brasil, a região de São Paulo (`sa-east-1`) responderia mais rápido, mas é mais cara. Num projeto de estudo, essa diferença de tempo não pesa.

## 6. Proposta

Usar a AWS, na região `us-east-1`, para toda a infraestrutura:

| Necessidade | Serviço na AWS | Repositório |
|---|---|---|
| Rede | VPC com sub-redes públicas e privadas em 2 zonas de disponibilidade | Monorepo (`kubernetes/`) |
| Kubernetes | Amazon EKS, com 2 nós `t3.small` | Monorepo (`kubernetes/`) |
| Entrada | Amazon API Gateway (HTTP API), VPC Link e NLB interno | Monorepo (`kubernetes/`) |
| Banco | Amazon RDS for PostgreSQL (`db.t3.micro`) | Monorepo (`DB/`) |
| Funções | AWS Lambda (.NET 10), publicadas com AWS SAM | LAMBDA |
| Imagens | Amazon ECR, criado manualmente | Monorepo (pipelines da raiz) |
| Estado do Terraform | Bucket S3 `terraform-state-soat16` | Monorepo (`kubernetes/` e `DB/`) |

## 7. Riscos e como reduzi-los

| Risco | Como reduzir |
|---|---|
| Dependência da AWS (API Gateway, Lambda e IAM) | A API é um container comum, e os manifests do Kubernetes e o PostgreSQL seguem padrões de mercado. Trocar de nuvem exigiria refazer o Terraform, não o código da API |
| Custo por hora do EKS, dos nós e do NLB | Destruir o ambiente depois da demonstração, com os workflows Destroy do LAMBDA e do monorepo |
| Custo extra se a versão do EKS sair do suporte padrão | Manter `eks_version` dentro do suporte padrão ([kubernetes/README.md](../../kubernetes/README.md#7-destruição-e-custos), seção 7) |
| NAT Gateway cobrado por hora | Não usar NAT: as sub-redes privadas ficam sem internet, e a autenticação usa duas Lambdas ([RFC-003](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md)) |

## 8. Resultado

Aceita e implementada no monorepo e no repositório LAMBDA. As decisões que partem desta estão na [RFC-002](RFC-002-Escolha-do-Banco-de-Dados.md) (banco), na [RFC-003](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md) (autenticação) e na [ADR-016](../adr/ADR-016-Escalabilidade-com-HPA.md) (escalabilidade com HPA).
