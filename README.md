# Projeto Oficina — Management

## 1. Identificação

Software de gestão para uma oficina mecânica.

Tech Challenge da Fase 4 do curso SOAT16 da FIAP. Este é o **monorepo do serviço Management**, com três partes: a aplicação (.NET), o banco (Amazon RDS) e o cluster com a borda (Amazon EKS e API Gateway). Na fase 3 essas partes ficavam em três repositórios separados (APP, DB e K8S). Só as Lambdas de autenticação continuam em um repositório próprio, o [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658).

Grupo:
-  Guilherme Toniello Vieira -  SOAT16 - rm374658

### 1.1 Estrutura do monorepo

```
/
├── server/               Solução .NET: Management API, camadas da Clean Architecture e testes
├── e2e/                  Collection do Postman da Management API e environment da AWS
├── docker-compose.yml    Postgres + Management API para rodar localmente
├── DB/                   Terraform do Amazon RDS PostgreSQL
├── kubernetes/           Terraform da VPC, EKS, API Gateway, NLB e addons (Datadog), e os manifests Kubernetes
├── docs/                 Arquitetura, observabilidade, ADRs e RFCs
└── .github/workflows/    Pipelines (build, testes, imagem, Terraform e deploy no cluster)
```

| Pasta | O que tem | Documentação |
|---|---|---|
| [server/](./server/) | Código .NET 10 da `TechChallenge.Oficina.Management.API` e testes | Seções 2 a 4 deste README |
| [e2e/](./e2e/) | Collection do Postman e environment da AWS | Seção 8 |
| [DB/](./DB/) | Amazon RDS PostgreSQL (state `techchallenge-oficina/fase-4/database.tfstate`) | [DB/README.md](./DB/README.md) |
| [kubernetes/](./kubernetes/) | `infra/foundation` (VPC, EKS, API Gateway, VPC Link e NLB), `infra/addons` (Metrics Server, Datadog Agent e recursos do Datadog) e `k8s/` (manifests da API, via Kustomize) | [kubernetes/README.md](./kubernetes/README.md) |
| [docs/](./docs/) | Visão geral, contrato de observabilidade, ADRs e RFCs | [docs/arquitetura.md](./docs/arquitetura.md) |
| [.github/workflows/](./.github/workflows/) | Bootstrap, Deploy, Destroy e K8s Apply | [.github/workflows/README.md](./.github/workflows/README.md) |

### 1.2 Links Úteis

- Blueprint no Miro: http://miro.com/app/board/uXjVHVfHuvI=/?share_link_id=633470424823
- Entregas anteriores. Os binários não foram trazidos para este repositório:
  - [relatório completo da Fase 2](https://github.com/GuiToniello/tech-challenge-fase-3-APP-soat16-rm374658/blob/main/docs/relat%C3%B3rio-completo.pdf), no repositório APP da fase 3;
  - [vídeo da Fase 2](https://youtu.be/BRWWTNrfGdY).

## 2. Arquitetura

A visão geral do sistema está em [docs/arquitetura.md](./docs/arquitetura.md). Lá estão:
- o diagrama de componentes (nuvem, API, banco e monitoramento);
- os diagramas de sequência da autenticação e da abertura de OS;
- o índice das RFCs e ADRs.

O código da API segue a Clean Architecture (ADR-001), com 4 anéis. As dependências apontam sempre para dentro:

```mermaid
flowchart LR
  subgraph FD[1 - Frameworks & Drivers]
    API[TechChallenge.Oficina.Management.API\nAPI / Web]
    DB[TechChallenge.Oficina.DB\nEF Core + PostgreSQL]
    Email[TechChallenge.Oficina.Email\nResend]
  end
  subgraph IA[2 - Interface Adapters]
    Controllers[TechChallenge.Oficina.Controllers]
    Adapters[TechChallenge.Oficina.Adapters]
  end
  subgraph AB[3 - Application Business Rules]
    UseCases[TechChallenge.Oficina.UseCases]
  end
  subgraph EB[4 - Enterprise Business Rules]
    Entities[TechChallenge.Oficina.Entities]
  end

  API --> Controllers
  API --> Adapters
  Adapters --> Controllers
  DB --> UseCases
  Email --> UseCases
  Controllers --> UseCases
  UseCases --> Entities
```

### 2.1. Visão Geral

As decisões arquiteturais estão documentadas como ADRs (Architecture Decision Records) em [docs/adr/](./docs/adr/). As RFCs (Request for Comments) são propostas que compararam opções antes de uma decisão e ficam em [docs/rfc/](./docs/rfc/). A numeração é compartilhada com o repositório LAMBDA, que guarda a RFC-003.

| ADR | Resumo | Link |
|-----|--------|------|
| ADR-001 — Clean Architecture com Folder-by-Feature | Adota Clean Architecture com 4 anéis de dependência (Entities, UseCases, Controllers, API/Infra), organizados por feature. Define separação de responsabilidades guiada pela regra de inversão de dependência. | [ver](./docs/adr/ADR-001-Clean-Architecture.md) |
| ADR-002 — DDD e Entidades Ricas | Entidades encapsulam comportamentos e validações de domínio via factory methods obrigatórios. Evita entidades anêmicas; erros de negócio são sinalizados com `DomainException`. | [ver](./docs/adr/ADR-002-DDD-e-Entidades-Ricas.md) |
| ADR-003 — Value Objects para CPF/CNPJ | CPF e CNPJ são encapsulados em Value Objects com validação de dígitos verificadores. `IdentificacaoCliente` detecta o tipo automaticamente e garante que apenas valores válidos existam no domínio. | [ver](./docs/adr/ADR-003-Value-Objects-Identificacao.md) |
| ADR-004 — Gateways e Persistência | Contratos de persistência (Gateways) residem nos UseCases; implementações ficam no projeto DB via EF Core. Leituras retornam coleções materializadas; escritas persistem via `SaveChangesAsync`. | [ver](./docs/adr/ADR-004-Gateways-e-Persistencia.md) |
| ADR-005 — Separação de Commands/Queries/ViewModels | Commands (escrita), Queries (leitura) e ViewModels (resposta) são contratos explícitos na camada de UseCases. A API consome esses contratos diretamente, sem criar os seus próprios. | [ver](./docs/adr/ADR-005-Separacao-Commands-Queries-ViewModels.md) |
| ADR-006 — AutoMapper nos UseCases | Mapeamento entre entidades de domínio e DTOs é responsabilidade exclusiva dos UseCases via AutoMapper. Controllers nunca instanciam `IMapper`; recebem apenas ViewModels já mapeadas. | [ver](./docs/adr/ADR-006-AutoMapper-nos-UseCases.md) |
| ADR-007 — EF Core com PostgreSQL | EF Core 10.x com Npgsql como ORM para PostgreSQL, com migrations versionadas. Configurações de mapeamento via `IEntityTypeConfiguration<T>` e connection string via `appsettings.json`. | [ver](./docs/adr/ADR-007-EFCore-com-PostgreSQL.md) |
| ADR-008 — Swagger para Documentação | Documentação automática de endpoints via Swashbuckle com suporte a autenticação JWT no UI. Swagger UI disponível na raiz da aplicação. | [ver](./docs/adr/ADR-008-Swagger-para-API.md) |
| ADR-009 — Migrações Automáticas no Startup | Migrations pendentes são aplicadas automaticamente ao iniciar a API via `ApplyMigrations()`. Elimina intervenção manual no deploy, incluindo ambientes de container. | [ver](./docs/adr/ADR-009-Migracoes-Automaticas-no-Startup.md) |
| ADR-011 — Envio de E-mail de Orçamento | Notifica o cliente por e-mail ao gerar orçamento via porta de saída `IOrcamentoEmailSender` e provedor Resend. A API funciona normalmente sem a ApiKey; o envio de e-mail é desabilitado. | [ver](./docs/adr/ADR-011-Envio-de-Email-de-Orcamento.md) |
| ADR-012 — Controle de Estoque de Insumos | Verifica estoque ao gerar orçamento (sem reserva) e debita automaticamente ao aprovar. Lança `DomainException` se houver estoque insuficiente em qualquer etapa. | [ver](./docs/adr/ADR-012-Controle-de-Estoque-de-Insumos.md) |
| ADR-013 — Tratamento de Erros em Duas Camadas | Erros de negócio esperados são tratados nos controllers com resposta HTTP semântica (4xx). Erros inesperados são capturados pelo middleware global, evitando vazamento de stack trace. | [ver](./docs/adr/ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md) |
| ADR-014 — Observabilidade com Datadog | A API escreve logs JSON no stdout (Serilog) com eventos de negócio, e o tracer é injetado no cluster, sem mudar a imagem. Métricas de negócio derivadas dos logs, monitores, synthetics e dashboard ficam em Terraform em `kubernetes/infra/addons`. | [ver](./docs/adr/ADR-014-Observabilidade-com-Datadog.md) |
| ADR-015 — Comunicação Síncrona via API Gateway | Os clientes falam com a API por HTTPS (REST e JSON), sempre pelo API Gateway. Não há filas nem mensageria. | [ver](./docs/adr/ADR-015-Comunicacao-Sincrona-via-API-Gateway.md) |
| ADR-016 — Escalabilidade da API com HPA | A API tem um HPA que mantém de 1 a 10 pods conforme o uso de CPU (alvo de 30%) e de memória (alvo de 150%), medido pelo Metrics Server. Os 2 nós do cluster são fixos. | [ver](./docs/adr/ADR-016-Escalabilidade-com-HPA.md) |
| ADR-017 — PostgreSQL no Amazon RDS e Modelo de Dados | Justificativa formal da escolha do PostgreSQL no Amazon RDS, com o diagrama ER e a explicação dos relacionamentos entre as tabelas. | [ver](./docs/adr/ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md) |
| ADR-018 — Monorepo e API Única (Management) | Junta os repositórios APP, DB e K8S da fase 3 neste monorepo, com pipelines unificados. Remove as 4 APIs dedicadas e renomeia a Monolith para Management. | [ver](./docs/adr/ADR-018-Monorepo-e-API-Unica-Management.md) |

| RFC | Resumo | Link |
|-----|--------|------|
| RFC-001 — Escolha da nuvem | Compara AWS, Azure e Google Cloud. Resultado: AWS, na região `us-east-1`. | [ver](./docs/rfc/RFC-001-Escolha-da-Nuvem.md) |
| RFC-002 — Escolha do banco de dados | Compara PostgreSQL, Aurora, MySQL, SQL Server, DynamoDB, MongoDB e PostgreSQL dentro do EKS. Resultado: PostgreSQL no Amazon RDS. | [ver](./docs/rfc/RFC-002-Escolha-do-Banco-de-Dados.md) |
| RFC-003 — Estratégia de autenticação (repo LAMBDA) | Compara onde validar o token e como gravar o CPF nele. Resultado: Lambda authorizer exigindo a claim `cpf`, com o JWT emitido pelo Auth0. | [ver](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md) |

### 2.2. Autenticação

O projeto usa autenticação via JWT emitido pelo `Auth0` (http://auth0.com/) como provedor de identidade.

**Como funciona na prática:**

A API valida cada requisição verificando o JWT no header `Authorization: Bearer <token>`. A integração usa o pacote `Microsoft.AspNetCore.Authentication.JwtBearer`. As configurações ficam em `appsettings.json` na chave `AuthSettings`:

```json
"AuthSettings": {
  "Authority": "<seu-dominio>.us.auth0.com",
  "Audience": "http://localhost:7194"
}
```

O `Authority` aponta para o domínio Auth0, que expõe o endpoint `/.well-known/openid-configuration` usado pelo middleware para buscar as chaves públicas de validação. O `Audience` identifica esta API junto ao Auth0.

Todos os endpoints exigem autenticação por padrão (política de fallback `RequireAuthenticatedUser`). Os únicos endpoints públicos são `/health` e `/health/ready`, que também verifica o banco (seção 6.3).

**Você não precisa criar uma conta no Auth0.** A Collection do Postman em `/e2e` já possui um request configurado com as credenciais de demonstração para obter o token de acesso via password grant (Resource Owner Password).

Na AWS, o API Gateway (em `kubernetes/infra/foundation`) tem um Lambda authorizer, do repositório LAMBDA, que exige, além do JWT válido, a claim `cpf` do cliente.
- **Como o token é gerado:** pela Lambda `POST /auth/token` (`{"email","senha","cpf"}`), que confere o cliente no banco e pede ao Auth0 o JWT já com a claim.
- **Exceção:** só o cadastro de cliente (`POST /management/api/clientes`) aceita o token direto do Auth0, sem `cpf`.
- **Na API:** o código não muda; ela continua validando o JWT normalmente.

O fluxo está no [diagrama de sequência da autenticação](./docs/arquitetura.md#3-diagrama-de-sequência-autenticação), e as opções avaliadas, na [RFC-003](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md) do repo LAMBDA.

### 2.3. CQRS (CQS)

O padrão implementado é o **CQS (Command Query Separation)** — o nível mais básico do CQRS — sem separação de banco de dados para leitura e escrita.

A separação acontece na **camada de UseCases** (`3 - Application Business Rules`). Cada feature é organizada com as seguintes pastas:

- `Commands/` — objetos de entrada para operações de escrita (ex: `CriarClienteCommand`, `AtualizarClienteCommand`)
- `Queries/` — objetos de entrada para operações de leitura (ex: `ListarClientesQuery`, `ObterClientePorIdQuery`)
- `ViewModels/` — objetos de saída (resposta da API)
- `UseCases/` — a classe que executa a operação, recebendo um `Command` ou uma `Query` como parâmetro tipado

O benefício é tornar a **intenção explícita no contrato**: ao receber um `Command`, sabe-se que haverá efeito colateral; ao receber uma `Query`, sabe-se que é apenas leitura. Não há mediator (ex: MediatR), o roteamento é feito diretamente via injeção de dependência.

## 3. Referências Bibliográficas

### 3.1. Padrões de Arquitetura

Esse projeto foi construído usando os padrões descritos abaixo.

- Clean Architecture (4 anéis e regra de dependência)
  - ADR: [ADR-001](./docs/adr/ADR-001-Clean-Architecture.md)
  - Codigo: [server/](./server/)
- Domain-Driven Design (DDD)
  - ADR: [ADR-002](./docs/adr/ADR-002-DDD-e-Entidades-Ricas.md)
  - Codigo: [Entidades de dominio](./server/4%20-%20Enterprise%20Business%20Rules/TechChallenge.Oficina.Entities/)
- CQS (separação entre Commands e Queries)
  - ADR: [ADR-005](./docs/adr/ADR-005-Separacao-Commands-Queries-ViewModels.md)
  - Codigo: [UseCases](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/)

### 3.2. Padrões Estruturais

- Folder-by-Feature
  - ADR: [ADR-001](./docs/adr/ADR-001-Clean-Architecture.md)
- Gateway/Repository para persistência
  - ADR: [ADR-004](./docs/adr/ADR-004-Gateways-e-Persistencia.md)
  - Codigo: [Gateways DB](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.DB/)
- Mapper/Adapter (AutoMapper nos UseCases)
  - ADR: [ADR-006](./docs/adr/ADR-006-AutoMapper-nos-UseCases.md)
  - Codigo: [UseCases](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/)
- Dependency Injection (Inversion of Control)
  - Codigo: [Program.cs](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Program.cs)
- Service Layer (aplicada na camada de UseCases)
  - Codigo: [UseCases](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/)

### 3.3. Padrões de Design

- Factory Method (entidades ricas com métodos de criação)
  - ADR: [ADR-002](./docs/adr/ADR-002-DDD-e-Entidades-Ricas.md)
- Value Object (CPF/CNPJ e identificação)
  - ADR: [ADR-003](./docs/adr/ADR-003-Value-Objects-Identificacao.md)
- DTO/ViewModel Pattern
  - ADR: [ADR-005](./docs/adr/ADR-005-Separacao-Commands-Queries-ViewModels.md)
- Centralized Exception Handling (duas camadas: controller + middleware)
  - ADR: [ADR-013](./docs/adr/ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md)
  - Codigo: [Middleware](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Middleware/) e [Controllers](./server/2%20-%20Interface%20Adapters/TechChallenge.Oficina.Controllers/)

### 3.4. Padrões Comportamentais

- CQS (Command Query Separation)
  - ADR: [ADR-005](./docs/adr/ADR-005-Separacao-Commands-Queries-ViewModels.md)
  - Implementacao: Commands (escrita), Queries (leitura), ViewModels (resposta)
- Domain Exceptions para regras de negócio e invariantes
  - ADR: [ADR-002](./docs/adr/ADR-002-DDD-e-Entidades-Ricas.md)
  - Codigo: [Exceptions](./server/4%20-%20Enterprise%20Business%20Rules/TechChallenge.Oficina.Entities/Exceptions/)

### 3.5. Referências

#### 3.5.1. Livros e Artigos

MARTIN, Robert C. Clean Architecture: A Craftsman's Guide to Software Structure and Design. Boston: Prentice Hall, 2017.

EVANS, Eric. Domain-Driven Design: Tackling Complexity in the Heart of Software. Boston: Addison-Wesley, 2003.

VERNON, Vaughn. Implementing Domain-Driven Design. Boston: Addison-Wesley, 2013.

FOWLER, Martin. Patterns of Enterprise Application Architecture. Boston: Addison-Wesley, 2003.

GAMMA, Erich; HELM, Richard; JOHNSON, Ralph; VLISSIDES, John. Design Patterns: Elements of Reusable Object-Oriented Software. Boston: Addison-Wesley, 1994.

MARTIN, Robert C. Agile Software Development, Principles, Patterns, and Practices. Upper Saddle River: Prentice Hall, 2002.

#### 3.5.2. Material de Apoio

FOWLER, Martin. CQRS. Martin Fowler, 14 jul. 2011. Disponível em: http://martinfowler.com/bliki/CQRS.html. Acesso em: 10 jun. 2026.

FOWLER, Martin. Inversion of Control Containers and the Dependency Injection pattern. Martin Fowler, 23 jan. 2004. Disponível em: http://martinfowler.com/articles/injection.html. Acesso em: 02 jun. 2026.

## 4. Executando o Projeto

Para executar o projeto localmente, na sua maquina dev, temos 3 alternativas descritas nas subseções `4.2`, `4.3` e `4.4`.

Siga apenas 1 delas.

### 4.1. Pré-requisito

Se você for rodar sem container, vai precisar:

- dotnet 10.x
http://dotnet.microsoft.com/pt-br/download/dotnet/thank-you/sdk-10.0.301-windows-x64-installer

- Postgres instalado e com instância ativa: http://www.postgresql.org/

Para containers, precisa do docker (http://www.docker.com/) ou Podman (http://podman.io/) instalado.

Já temos docker-compose pronto com todas as configurações.

> Recomenda-se o uso de containers

### 4.1.1. Configuração por .env (recomendado para container)

Existe um arquivo de exemplo na raiz do projeto: `.env.example`.

Passo 1 - gere o arquivo `.env` a partir do exemplo.

- Linux/macOS: `cp .env.example .env`
- Windows PowerShell: `Copy-Item .env.example .env`

Passo 2 - ajuste as variáveis conforme necessário, principalmente:

- `POSTGRES_PORT`
- `API_MANAGEMENT_PORT`
- `DATABASE_CONNECTION_STRING`
- `RESEND_API_KEY` (opcional)

> O `.env.example` traz `REGISTRY_PREFIX` apontando para o ECR. Localmente, isso só muda o nome da imagem gerada. Se preferir um nome curto, deixe a variável vazia.

### 4.2. Alternativa A - Containers (docker, podman, ...)

Passo 1 - Com o console apontado para o root do repositório, execute `docker compose up -d --build`

Se estiver usando o podman, use `podman compose up -d --build`

E pronto!

O banco de dados `postgres` e a `management-api` estarão disponíveis.

Passo 2 - Use `http://localhost:7194/index.html` para acessar o swagger.

### 4.3. Alternativa B - Local com dotnet cli

Passo 1 - rode o `postgres` - pode ser uma instância local ou via container `docker compose up -d postgres`.

Passo 2 - aponte o console para a pasta `server/1 - Frameworks & Drivers/TechChallenge.Oficina.Management.API/` e então execute `dotnet run`

Pronto!

A API sobe em `http://localhost:7194`, conforme o perfil `http` do `launchSettings.json`.

Passo 3 - Use `http://localhost:7194/index.html` para acessar o Swagger.

### 4.4. Alternativa C - Local com Visual Studio 2026 ou VS Code

Passo 1 - rode o `postgres` - pode ser uma instância local ou via container `docker compose up -d postgres`.

Passo 2 - abra o arquivo `.slnx` em `/server`

Passo 3 - no Visual Studio, rode usando o perfil `http`.

Passo 4 - Use `http://localhost:7194/index.html` para acessar o Swagger.


### 4.5. Banco de dados

Para popular o banco de dados, use a Collection do Postman na pasta `/e2e`.

Não temos scripts SQL ou endpoint específico, apenas use a collection que ela irá popular o banco e executar demais operações de demonstração.

Se você estiver rodando em container, não é preciso configurar mais nada, apenas rodar, a connection string já está certa.

Se você está rodando o Postgres localmente, com instância não containerizada, precisa configurar a connection string em `server/1 - Frameworks & Drivers/TechChallenge.Oficina.Management.API/appsettings.json`, na chave `DatabaseSettings:ConnectionString`.

Na AWS, o banco é o Amazon RDS criado pelo Terraform de [DB/](./DB/README.md).

### 4.6. Rodando os testes

Os testes automatizados estão organizados em `server/Tests/` por camada da arquitetura. Eles usam banco em memória e mocks, sem precisar de Postgres ou Docker.

Para executar todos os testes da solução, use:

`dotnet test ./server/TechChallenge.Oficina.API.slnx`

Se quiser executar apenas um projeto de teste específico, use o `.csproj` correspondente em `server/Tests/`.

### 4.7. Problemas comuns (troubleshooting)

1. Porta 5432 em uso
Se o Postgres não subir na porta padrão, altere `POSTGRES_PORT` no arquivo `.env` (por exemplo, `5433`) e ajuste também `DATABASE_CONNECTION_STRING` para usar a mesma porta.

2. Erro de autenticação JWT (401)
Verifique se `AuthSettings:Authority` e `AuthSettings:Audience` no `appsettings.json` correspondem exatamente ao token usado pela collection.

3. E-mail não está sendo enviado
Sem `RESEND_API_KEY` (container) ou `ApiKey` no `appsettings.json` (execução local), a API continua funcionando, mas o envio de e-mail fica desabilitado por configuração. O e-mail de mudança de status também depende de `ResendSettings:SendEmailOnStatusChange`, que é `false` nos `appsettings.json`. Para ligá-lo, defina `ResendSettings__SendEmailOnStatusChange=true`.

## 5. Requisitos Implementados

Abaixo, você encontra uma tabela com o resumo dos requisitos implementados: os de negócio e os de observabilidade (R1 a R7). Todos são atendidos pela Management API. As rotas abaixo ficam sob `/api/ordens-servico`; na AWS, elas ganham o prefixo `/management` do API Gateway.

| Requisito | Atende? | Observação de escopo | Rota | Evidências |
|---|---|---|---|---|
| Consulta de status da Ordem de Serviço (OS) | Sim | Acompanhamento da OS com o status atual. | `GET /api/ordens-servico/{id}/acompanhamento` | [Endpoints OS](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Features/OrdensServico/OrdensServicoEndpoints.cs) · [Controller OS](./server/2%20-%20Interface%20Adapters/TechChallenge.Oficina.Controllers/Features/OrdensServico/OrdensServicoController.cs) |
| Aprovação/recusa de orçamento por endpoint externo | Sim | Aprovação e recusa implementadas no fluxo da OS. | `POST /api/ordens-servico/{id}/aprovar-orcamento` e `POST /api/ordens-servico/{id}/recusar-orcamento` | [Endpoints OS](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Features/OrdensServico/OrdensServicoEndpoints.cs) · [UseCases OS](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/Features/OrdensServico/UseCases/OrdemServicoUseCases.cs) |
| Abertura de OS com cliente, veículo, serviços e peças, retornando ID único | Sim | Abertura completa com retorno da identificação da OS. | `POST /api/ordens-servico/completa` | [Controller OS](./server/2%20-%20Interface%20Adapters/TechChallenge.Oficina.Controllers/Features/OrdensServico/OrdensServicoController.cs) · [ViewModel Abertura](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/Features/OrdensServico/ViewModels/AberturaOrdemServicoViewModel.cs) |
| Listagem de OS por prioridade + antiguidade, excluindo finalizadas/entregues | Sim | Escopo acordado: endpoint ordenado /ordenadas. | `GET /api/ordens-servico/ordenadas` | [Gateway OS](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.DB/Features/OrdensServico/OrdemServicoGateway.cs) · [Query Ordenada](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/Features/OrdensServico/Queries/ListarOrdensServicoOrdenadasQuery.cs) |
| Atualização de status da OS com notificação ao cliente | Sim | A cada mudança de status da OS, o cliente é notificado por e-mail no fluxo de controller/use case. | `POST /api/ordens-servico/{id}/em-diagnostico`, `/em-execucao`, `/finalizar` e `/entregar` | [Endpoints OS](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Features/OrdensServico/OrdensServicoEndpoints.cs) · [UseCases OS](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/Features/OrdensServico/UseCases/OrdemServicoUseCases.cs) · [Sender Status](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Email/Features/OrdensServico/OrdemServicoStatusEmailSender.cs) |
| R1 — Latência da API | Sim | APM com o tracer .NET injetado pelo Datadog Agent no cluster, sem mudança no código ou no Dockerfile. Latência p95 por serviço no monitor e no dashboard; por rota, na página do serviço no APM. | `service` = `management-api` | [ADR-014](./docs/adr/ADR-014-Observabilidade-com-Datadog.md) · [kubernetes/infra/DATADOG.md](./kubernetes/infra/DATADOG.md) |
| R2 — Consumo de recursos do Kubernetes na AWS | Sim | Datadog Agent (DaemonSet + Cluster Agent) instalado pelo `kubernetes/infra/addons`. Monitores de CPU e memória em relação ao limit, restarts, CrashLoopBackOff e nós, mais widgets. | Cluster EKS | [ADR-014](./docs/adr/ADR-014-Observabilidade-com-Datadog.md) · [kubernetes/infra/DATADOG.md](./kubernetes/infra/DATADOG.md) |
| R3 — Healthchecks e uptime | Sim | `/health/ready` verifica o banco e é consultado pelo `http_check` do Datadog Agent; o `/health` não muda. Uptime externo por Synthetics em `GET /management/health` pelo API Gateway, com SLO. | `/health` e `/health/ready` | [Program.cs](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Program.cs) · [Health check do banco](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.DB/Extensions.cs) · [docs/observabilidade.md](./docs/observabilidade.md) (seção 6) · [kubernetes/infra/DATADOG.md](./kubernetes/infra/DATADOG.md) |
| R4 — Falhas no processamento de OS | Sim | Eventos de log `os.falha` por etapa (regra de negócio), `integracao.falha` (Resend) e `http.erro_nao_tratado` (500, identificando banco indisponível), mais 5xx por rota no APM. Viram métricas, monitores e widgets. | Fluxo de OS | [docs/observabilidade.md](./docs/observabilidade.md) (seções 3 e 5) · [Eventos OS](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/Features/OrdensServico/Logging/OrdemServicoLogEventos.cs) · [Controller OS](./server/2%20-%20Interface%20Adapters/TechChallenge.Oficina.Controllers/Features/OrdensServico/OrdensServicoController.cs) · [UseCases OS](./server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.UseCases/Features/OrdensServico/UseCases/OrdemServicoUseCases.cs) · [Middleware](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Middleware/GlobalExceptionHandlerMiddleware.cs) |
| R5 — Logs estruturados (JSON) | Sim | Serilog com 1 JSON por linha no stdout e chaves que o Datadog reconhece sem configuração (`timestamp`, `level`, `message`, `evento`, `dd.*` e `error.*`). | Toda a API | [LoggingExtension](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Extensions/LoggingExtension.cs) · [docs/observabilidade.md](./docs/observabilidade.md) (seção 2) |
| R6 — Correlação entre logs e traces | Sim | Dentro da API: o tracer injeta `dd.trace_id` e `dd.span_id` em cada log, que abre o trace no APM. API Gateway e Lambdas não são rastreados (limitação registrada na ADR-014). | Toda a API | [LoggingExtension](./server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Management.API/Extensions/LoggingExtension.cs) · [ADR-014](./docs/adr/ADR-014-Observabilidade-com-Datadog.md) |
| R7 — Dashboard | Sim | 1 dashboard: OS criadas e encerradas por dia, tempo médio por status, falhas (de negócio por etapa e 5xx por rota), erros de integração e seções técnicas de R1 a R3. Os widgets de negócio só recebem dados durante o trial de 14 dias do Datadog. | Datadog | [docs/observabilidade.md](./docs/observabilidade.md) (seção 5) · [kubernetes/infra/DATADOG.md](./kubernetes/infra/DATADOG.md) |

## 6. Infraestrutura

A infraestrutura fica em duas pastas deste monorepo. As Lambdas de autenticação ficam no repositório LAMBDA.

| Parte | Onde | Responsabilidade |
|---|---|---|
| Aplicação | [server/](./server/) | Código .NET, testes e imagem Docker, publicada no Amazon ECR |
| Cluster e borda | [kubernetes/](./kubernetes/README.md) | VPC, Amazon EKS, Amazon API Gateway (acesso externo à API), VPC Link e NLB interno, addons (Metrics Server e Datadog Agent), recursos do Datadog (métricas de log, monitores, synthetics e dashboard) e manifests Kubernetes da API. O pipeline gera o Secret com a connection string do RDS e a chave do Resend |
| Banco | [DB/](./DB/README.md) | Amazon RDS PostgreSQL. O schema é criado pelas migrations da API, no startup (ADR-009) |
| Autenticação | [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658) (outro repositório) | Lambda authorizer do API Gateway (JWT do Auth0 com a claim `cpf`) e Lambda `POST /auth/token`, que gera o JWT com o CPF do cliente |

**Ordem para subir:** o **Bootstrap** deste repositório roda tudo na sequência: testes → foundation → addons → RDS → imagem no ECR → manifests. Depois, rode o **Deploy do repo LAMBDA**, que usa a VPC e o RDS criados aqui.

**Ordem para derrubar:** primeiro o **Destroy do repo LAMBDA**, depois o **Destroy deste repositório**, que remove RDS → addons → foundation.

O ECR é criado manualmente e fica fora de qualquer `destroy` (6.2).

```mermaid
flowchart LR
  subgraph Mono[Este monorepo]
    Code[server/\nCodigo + testes] --> Image[docker compose build\nmanagement-api]
    Infra[kubernetes/infra\nVPC, EKS, API Gateway, NLB, addons]
    DBTF[DB/infra\nRDS]
    Manifests[kubernetes/k8s\nmanifests]
  end

  Image -->|push :latest| ECR[Amazon ECR\ncriado manualmente]
  Infra --> EKS[Amazon EKS\nPods da management-api]
  Manifests -->|kubectl apply -k + rollout restart| EKS
  EKS -->|pull| ECR
  EKS -->|PostgreSQL 5432| RDS[Amazon RDS]
  DBTF --> RDS
  LAMBDA[Repo LAMBDA\nauthorizer e /auth/token] -.->|usa a VPC e o RDS| RDS
```

### 6.1 Contrato entre as partes

| Item | Valor |
|---|---|
| Imagem | `903936907231.dkr.ecr.us-east-1.amazonaws.com/techchallenge-oficina-management:latest`, o mesmo nome do `docker-compose.yml`. O manifest em `kubernetes/k8s` usa `:latest` com `imagePullPolicy: Always` |
| Porta e health | A API escuta em `8080` e expõe `/health` sem autenticação. Ele é usado pelas probes do Kubernetes, pelo health check do NLB do API Gateway e pelos synthetics do Datadog |
| Health com banco | A API expõe `/health/ready` sem autenticação: verifica o banco e responde `Healthy` (200) ou `Unhealthy` (503). É consultado só pelo `http_check` do Datadog Agent. O `readinessProbe` e o NLB continuam em `/health`, que não depende do banco |
| Rotas | As rotas ficam na raiz (`/api/...` e `/health`), sem `PathBase`. O API Gateway recebe `/management/...` e remove o prefixo antes de encaminhar. `POST /auth/token` vai para a Lambda do repo LAMBDA |
| Configuração | Na AWS, `DatabaseSettings__ConnectionString` e `ResendSettings__ApiKey` vêm do Secret `oficina-api-secrets`, gerado pelo pipeline (`_k8s-apply.yml`). O ConfigMap `management-api-config` define só `ASPNETCORE_*`, `AllowedHosts` e o nível de log. Ele sobrescreve a seção `Serilog` do `appsettings.json` com `Serilog__MinimumLevel__Default` e `Serilog__MinimumLevel__Override__Microsoft.AspNetCore`. `AuthSettings` e o restante de `ResendSettings` (`FromEmail`, `SendEmailOnStatusChange`) vêm do `appsettings.json` da imagem |
| Logs | JSON no stdout, 1 por linha (Serilog), com `timestamp`, `level`, `message`, `evento`, `logger.name`, `dd.*`, `error.*` e as propriedades dos eventos. O Datadog Agent coleta o stdout dos pods. As métricas de log, os monitores e o dashboard dependem dos nomes de eventos, propriedades e valores de [docs/observabilidade.md](./docs/observabilidade.md) |
| Datadog | O Deployment tem as labels `tags.datadoghq.com/env: study` e `tags.datadoghq.com/service: management-api`, que definem as tags `env` e `service` de logs, traces e métricas. O Agent injeta o tracer .NET nos pods do namespace `oficina` |
| Banco | As migrations do EF Core rodam no startup da API. Com um RDS novo (vazio), as tabelas são criadas quando a API sobe. A Lambda `buscar-cliente` do repo LAMBDA lê a tabela `clientes` |

### 6.2 ECR (manual)

Crie uma vez, em `us-east-1`, o repositório privado `techchallenge-oficina-management` com *tag mutability* **MUTABLE**. A tag `latest` é sobrescrita a cada push, e com IMMUTABLE o segundo push falha.

### 6.3 Observabilidade (Datadog)

A API escreve logs em JSON no stdout, 1 por linha (Serilog):
- com eventos de negócio do fluxo de OS (`os.criada`, `os.status_alterado`, `os.falha` etc.);
- com erros em `error.*`.

Na AWS, o Datadog Agent (instalado por `kubernetes/infra/addons`) coleta esses logs e injeta o tracer .NET nos pods (APM). Isso não exige mudança no código nem no Dockerfile, e cada log traz o `dd.trace_id` da requisição. O `/health/ready` verifica o banco para o `http_check` do Agent.

- Decisão, consequências e limitações: [ADR-014](./docs/adr/ADR-014-Observabilidade-com-Datadog.md).
- Contrato entre a API e o Datadog: [docs/observabilidade.md](./docs/observabilidade.md). Cobre o formato do log, o catálogo de eventos, os valores permitidos, o mapa evento → métrica → dashboard e consultas úteis.
- Agent, métricas, monitores, synthetics, dashboard, secrets e liga/desliga (`DD_ENABLED`): [kubernetes/infra/DATADOG.md](./kubernetes/infra/DATADOG.md).

Localmente, o `docker compose` mostra o mesmo JSON, só sem o objeto `dd`, porque não há tracer. A conta do Datadog é um trial de 14 dias; depois dele, APM, logs e synthetics param (ADR-014).

## 7. Fluxo de Deploy (CI/CD)

O CI/CD usa GitHub Actions, com pipelines únicos para a aplicação, o banco e o cluster. Os detalhes estão em [.github/workflows/README.md](./.github/workflows/README.md).

- **Bootstrap** (manual): testes → foundation → addons → RDS → build e push da imagem → `kubectl apply` com restart. Use na primeira criação, para recriar o ambiente depois do Destroy e para reaplicar a HEAD da `main`.
- **Deploy**:
  - **Pull Request para a `main`:** build e testes, validação offline do Terraform (foundation, addons e DB) e do Kustomize. Também roda os `plan` informativos.
  - **Push na `main`:** aplica só o que mudou, na ordem: foundation → addons → RDS → imagem → manifests. O passo dos manifests reinicia a API.
- **Destroy** (manual, com `confirm=destroy` e aprovação no environment `destroy`): confere que as Lambdas já foram destruídas e remove RDS → addons → foundation.
- **K8s Apply** (manual): reaplica os manifests e reinicia a API. Use, por exemplo, depois de trocar `RESEND_API_KEY`.
- **Trocar `RDS_PASSWORD`:** rode o Bootstrap, que aplica a senha nova no RDS e recria o Secret da API. Depois rode o Deploy do repo LAMBDA.

```mermaid
flowchart LR
  subgraph Reusable[Workflows reutilizaveis]
    BT[_build-test.yml\nrestore, build e testes]
    TF[_terraform.yml\nfoundation, addons e DB]
    BP[_build-push.yml\ndocker compose build/push]
    KA[_k8s-apply.yml\nkustomize / kubectl apply]
  end

  B[Bootstrap\nmanual] --> BT --> TF --> BP --> KA
  PR[Deploy - pull_request] -->|build-test + validate + plan| BT
  PUSH[Deploy - push na main] -->|so o que mudou| TF
  D[Destroy\nmanual] -->|DB, addons, foundation| TF
```

Apply, destroy e o push da imagem só rodam a partir da `main`: disparados em outra branch, os jobs falham com erro.

**Atenção ao primeiro push na `main`:** todos os arquivos entram como alterados. Por isso, o Deploy tenta aplicar tudo: foundation, addons, RDS, imagem e manifests. Isso **cria o ambiente inteiro na AWS (custo)**.
- **Se ainda não quiser subir o ambiente:** faça o primeiro push sem os secrets da AWS configurados. Os jobs de apply falham logo na credencial e não criam nada.
- **Quando quiser subir:** configure tudo e rode o Bootstrap.

Antes de subir o ambiente:
- crie o ECR (6.2);
- configure os secrets, as variables e o environment `destroy`;
- libere ao usuário IAM `terraform` as novas chaves de state (`techchallenge-oficina/fase-4/*` no bucket `terraform-state-soat16`).

Tudo isso está em [.github/workflows/README.md](./.github/workflows/README.md).

**Desative também os workflows dos repositórios APP, DB e K8S da fase 3.** Eles usam os mesmos nomes de recursos na AWS e publicam no mesmo ECR. O ambiente da fase 3 precisa estar destruído antes do primeiro Bootstrap deste repositório.

## 8. Finalização

Nessa seção, você encontra observações gerais.

- Para fazer requisições, use a Collection do Postman na pasta `/e2e` (`TechChallenge.Oficina.Management.API.postman_collection.json`)
  - importe a collection no Postman
  - Em `variables`, altere para apontar a URL para onde está rodando o projeto.
  - Na AWS:
    - importe também o environment `e2e/AWS.postman_environment.json` e selecione-o;
    - preencha só `gatewayUrl`, com a URL do API Gateway (output `api_gateway_endpoint` de `kubernetes/infra/foundation`);
    - o environment monta o `baseUrl` com o prefixo `/management` e o `tokenUrl` (`/auth/token`);
    - o endpoint só aceita HTTPS, e o Swagger UI não abre atrás do prefixo (use a collection).
  - O "00 - Setup" da collection:
    1. pega o token direto do Auth0;
    2. cadastra um cliente com ele;
    3. gera, pela Lambda `/auth/token`, o JWT com a claim `cpf` desse cliente, usado em todos os requests seguintes.
  - Rodando local (sem gateway), `tokenUrl` fica vazio: a geração pela Lambda é pulada e a collection segue com o token direto do Auth0.

- Para o envio de e-mails, é preciso configurar `ApiKey` no appsettings.json ou `ResendSettings__ApiKey` para container

Você pode criar uma conta em http://resend.com/ e gerar a ApiKey.

Sem a ApiKey, a API funciona normalmente, só não envia os e-mails.
