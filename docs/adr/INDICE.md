# ADRs - Architecture Decision Records

A visão geral da arquitetura (diagramas de componentes e de sequência) e o índice das RFCs estão em [docs/arquitetura.md](../arquitetura.md).

## Índice

1. [ADR-001: Clean Architecture com Folder-by-Feature](ADR-001-Clean-Architecture.md)
2. [ADR-002: Domain-Driven Design e Entidades Ricas](ADR-002-DDD-e-Entidades-Ricas.md)
3. [ADR-003: Value Objects para Validação de Identificação (CPF/CNPJ)](ADR-003-Value-Objects-Identificacao.md)
4. [ADR-004: Gateways e Persistência](ADR-004-Gateways-e-Persistencia.md)
5. [ADR-005: Separação de Contratos em Commands/Queries/ViewModels](ADR-005-Separacao-Commands-Queries-ViewModels.md)
6. [ADR-006: Mapeamento com AutoMapper na Application (UseCases)](ADR-006-AutoMapper-nos-UseCases.md)
7. [ADR-007: Entity Framework Core com PostgreSQL](ADR-007-EFCore-com-PostgreSQL.md)
8. [ADR-008: Swagger para Documentação de API](ADR-008-Swagger-para-API.md)
9. [ADR-009: Migrações Automáticas no Startup](ADR-009-Migracoes-Automaticas-no-Startup.md)
10. [ADR-011: Envio de E-mail de Orçamento com Resend](ADR-011-Envio-de-Email-de-Orcamento.md)
11. [ADR-012: Controle de Estoque de Insumos](ADR-012-Controle-de-Estoque-de-Insumos.md)
12. [ADR-013: Estratégia de Tratamento de Erros em Duas Camadas](ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md)
13. [ADR-014: Observabilidade com Datadog](ADR-014-Observabilidade-com-Datadog.md)
14. [ADR-015: Comunicação Síncrona (HTTP/REST) via API Gateway](ADR-015-Comunicacao-Sincrona-via-API-Gateway.md)
15. [ADR-016: Escalabilidade da API com HPA](ADR-016-Escalabilidade-com-HPA.md)
16. [ADR-017: PostgreSQL no Amazon RDS e Modelo de Dados](ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md)
17. [ADR-018: Monorepo e API Única (Management)](ADR-018-Monorepo-e-API-Unica-Management.md)
18. [ADR-019: OS Service, Microsserviço com Banco Próprio](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md) (repo OS-SERVICE)
19. [ADR-020: Eventos com Amazon SQS e Saga Coreografada](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)

### RFCs

A numeração das ADRs e RFCs é compartilhada entre este monorepo e os repositórios OS-SERVICE e LAMBDA. As RFCs deste monorepo ficam em `docs/rfc/`; a ADR-019 fica no repo OS-SERVICE, e a RFC-003, no repo LAMBDA:

- [RFC-001: Escolha da nuvem](../rfc/RFC-001-Escolha-da-Nuvem.md)
- [RFC-002: Escolha do banco de dados](../rfc/RFC-002-Escolha-do-Banco-de-Dados.md)
- [RFC-003: Estratégia de autenticação](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md) (repo LAMBDA)
- [RFC-004: Comunicação entre microsserviços](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md)

## Resumo de Contextos

### Contexto de Arquitetura
- **ADR-001**: Clean Architecture com folder-by-feature e regra de dependência
- **ADR-004**: Portas de saída (Gateways) de persistência no projeto Application (UseCases) com implementação no projeto DB

### Contexto de Domain-Driven Design
- **ADR-002**: Entidades ricas com comportamento validador
- **ADR-003**: Value Objects para validação de identificação

### Contexto de Separação de Responsabilidades
- **ADR-005**: Contratos segregados (Commands/Queries/ViewModels)
- **ADR-006**: AutoMapper centralizado no projeto Application (UseCases)

### Contexto de Infraestrutura de Dados
- **ADR-007**: EF Core + PostgreSQL com migrations versionadas
- **ADR-009**: Migrações automáticas no startup
- **ADR-017**: PostgreSQL no Amazon RDS, com justificativa formal, diagrama ER e relacionamentos do banco da Management (sem as tabelas de OS desde a fase 4)
- **ADR-019**: Um banco por serviço: o OS Service tem o próprio RDS (repo OS-SERVICE)

### Contexto de Comunicação entre Componentes
- **ADR-015**: Comunicação síncrona (HTTP/REST) dos clientes com as APIs, com entrada única pelo API Gateway e sem chamadas HTTP entre as APIs
- **ADR-020**: Entre os serviços, eventos em filas Amazon SQS FIFO e saga coreografada com compensação (opções comparadas na RFC-004)

### Contexto de Escalabilidade
- **ADR-016**: HPA de cada API, de 1 a 5 pods conforme CPU e memória, com os nós do cluster fixos

### Contexto de Organização do Repositório e dos Serviços
- **ADR-018**: Monorepo com a aplicação, o banco e o cluster (antes os repositórios APP, DB e K8S), pipelines unificadas e uma só API neste monorepo, a Management API
- **ADR-019**: O ciclo de vida da OS e os indicadores num microsserviço próprio, o OS Service, com repositório, banco, namespace e pipelines próprios (repo OS-SERVICE)

### Contexto de Exposição da API
- **ADR-008**: Swagger para documentação auto-atualizada

### Contexto de Comunicação com Cliente
- **ADR-011**: Envio de orçamento por e-mail via Resend com porta de saída na Application (UseCases); e-mail de mudança de status disparado por evento desde a fase 4

### Contexto de Controle de Estoque
- **ADR-012**: Verificação e débito de estoque de insumos ao gerar e aprovar o orçamento

### Contexto de Segurança e Resiliência
- **ADR-013**: Tratamento de erros esperados na borda (endpoints + adapters/controllers da arquitetura) e erros inesperados no middleware global

### Contexto de Observabilidade
- **ADR-014**: Logs JSON no stdout (Serilog) com eventos de negócio, APM injetado no cluster e métricas, monitores e dashboard no Datadog, para as duas APIs
