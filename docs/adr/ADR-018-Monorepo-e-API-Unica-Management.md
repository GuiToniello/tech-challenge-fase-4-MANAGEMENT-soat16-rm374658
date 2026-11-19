# ADR-018: Monorepo e API Única (Management)

**Status**: Aceita (fase 4). Atualizada: ainda na fase 4, o ciclo de vida da OS foi extraído para um segundo serviço, o OS Service, com repositório e banco próprios ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)). Depois, o orçamento foi para um terceiro serviço, o Billing, também com repositório e banco próprios ([ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md)), e a fila de produção (diagnóstico e execução), para um quarto, o Execução e Produção, também com repositório e banco próprios ([ADR-023](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md)). A "API única" vale para este monorepo, que continua com uma só API, a Management API; o sistema passou a ter quatro.

**Contexto**:
Na fase 3 o sistema ficou dividido em quatro repositórios, cada um com as próprias pipelines e state do Terraform:

- **APP:** a solução .NET com 5 APIs (a Monolith e quatro APIs dedicadas: ApprovalService, CreateOSService, GetOSService e StatusService).
- **DB:** o RDS PostgreSQL.
- **K8S:** VPC, EKS, API Gateway, NLB, addons e manifests.
- **LAMBDA:** o authorizer e a emissão do token.

A ordem entre os repositórios era manual (K8S → DB → APP → K8s Apply). O APP disparava o K8s Apply do K8S com um token pessoal (`K8S_REPO_TOKEN`). O mesmo `RDS_PASSWORD` precisava ser cadastrado em mais de um repositório.

As quatro APIs dedicadas não tinham código próprio nas camadas compartilhadas. Elas só reexpunham rotas que a Monolith já tinha (`/completa`, `/ordenadas`, `/{id}/acompanhamento`, `/aprovar-orcamento` e `/recusar-orcamento`), com o mesmo banco. Para a fase 4, a aplicação passa a ser o serviço de gestão da oficina (**Management**).

**Decisão**:

1. **Um monorepo para aplicação e infraestrutura** (`tech-challenge-fase-4-MANAGEMENT-soat16-rm374658`):
   - Os repositórios APP, DB e K8S da fase 3 viram pastas:
     - a aplicação fica na raiz (`server/`, `e2e/`, `docker-compose.yml`);
     - o banco vai para `DB/`;
     - o cluster e a borda vão para `kubernetes/`.
   - O histórico de commits não foi migrado.
   - A LAMBDA continua em repositório próprio ([tech-challenge-fase-4-LAMBDA-soat16-rm374658](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658)).
2. **Uma API só neste monorepo:**
   - A `TechChallenge.Oficina.Monolith.API` foi renomeada para `TechChallenge.Oficina.Management.API`.
   - As APIs dedicadas, os testes, as collections, as imagens, os manifests e as rotas delas foram removidos.
   - No cluster, no ECR, no Datadog e no API Gateway, o nome passa a ser `management-api`, a imagem `techchallenge-oficina-management` e o prefixo `/management`.
   - Depois, as rotas de OS e os indicadores saíram da Management API para o OS Service (ADR-019), e as do orçamento, para o Billing (ADR-021). Os serviços conversam por eventos no SQS ([ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)). A rota `/completa` deixou de existir.
3. **Pipelines unificados** em `.github/workflows/`:
   - **Bootstrap:** sobe tudo na ordem (testes → foundation → addons → RDS → imagem → manifests).
   - **Deploy:**
     - em PR, valida tudo;
     - em push na `main`, aplica só as partes que mudaram, na mesma ordem.
   - **Destroy:** derruba tudo na ordem inversa.
   - A ordem entre "repositórios" virou `needs` entre jobs, e o `K8S_REPO_TOKEN` deixou de existir.
4. **State novo:** as chaves do Terraform ganharam o prefixo `techchallenge-oficina/fase-4/`, no mesmo bucket `terraform-state-soat16`. Os nomes dos recursos na AWS não mudaram, então o ambiente da fase 3 precisa estar destruído antes do Bootstrap.

**Consequências**:
- ✅ Uma mudança que atravessa aplicação, banco e cluster sai em um único PR e um único pipeline, na ordem certa.
- ✅ Menos peças neste monorepo: uma imagem, um Deployment, um HPA, uma rota no gateway, um target group, um synthetic. Também há menos secrets duplicados. O OS Service, o Billing e o Execução e Produção somam os deles, nos próprios repositórios.
- ✅ Os docs ficam juntos: ADRs e RFCs em [docs/](../), e um README por pasta.
- ⚠ Um push em `kubernetes/infra/**` com o ambiente desligado recria o cluster (custo), como já acontecia na fase 3.
- ⚠ A LAMBDA continua acoplada por nomes fixos: o prefixo `/management` na rota liberada sem `cpf` (`POST /management/api/clientes`), o SG do RDS e os nomes das funções. Com os outros serviços, a ordem entre os repositórios continua manual:
  - para subir: Bootstrap deste repo → Bootstrap do OS-SERVICE → Bootstrap do EXECUTION (o OS Service precisa consumir os eventos novos antes de o Execução e Produção publicá-los), com o do BILLING a qualquer momento depois deste → Deploy da LAMBDA;
  - para derrubar: Destroy da LAMBDA, do OS-SERVICE, do BILLING e do EXECUTION (em qualquer ordem) → Destroy deste repo.
- ⚠ O state da fase 3 fica no bucket com as chaves antigas, só como histórico.

**Relacionados**: [ADR-001](ADR-001-Clean-Architecture.md), [ADR-015](ADR-015-Comunicacao-Sincrona-via-API-Gateway.md), [ADR-016](ADR-016-Escalabilidade-com-HPA.md), [ADR-017](ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md), [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md) (repo OS-SERVICE), [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md), [ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md) (repo BILLING), [ADR-023](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md) (repo EXECUTION).
