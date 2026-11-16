# ADR-015: Comunicação Síncrona (HTTP/REST) via API Gateway

**Status**: Aceita (atualizada na fase 4, ver [ADR-018](ADR-018-Monorepo-e-API-Unica-Management.md) e [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md); o item 7 foi superado pela [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md))

**Resumo**:
Os clientes falam com as APIs de forma **síncrona**: quem chama espera a resposta na hora. As chamadas usam HTTPS, no estilo REST e com JSON, sempre pela mesma porta de entrada, o Amazon API Gateway. Até a fase 3, havia uma só API e nenhuma fila. Na fase 4, o ciclo de vida da OS foi para um segundo serviço, o OS Service, e a comunicação **entre os serviços** passou a ser por eventos no Amazon SQS ([ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)). Para os clientes, nada muda. O desenho geral está no [diagrama de componentes](../arquitetura.md#2-diagrama-de-componentes).

**Contexto**:
O sistema tem duas APIs .NET no Amazon EKS (a Management API e, desde a fase 4, o OS Service, do repo OS-SERVICE), as Lambdas de autenticação (repo LAMBDA), um banco PostgreSQL no Amazon RDS para cada API e três serviços externos: Auth0, Resend e Datadog. Era preciso definir:
- como os clientes chegam às APIs;
- como as partes internas trocam dados entre si;
- como o sistema fala com os serviços externos.

Fatores de decisão:
- simplicidade: poucas peças para entender, testar e operar;
- custo baixo: projeto de estudo, com um cluster pequeno (2 nós `t3.small`);
- segurança: uma única entrada pública, com autenticação logo na entrada;
- os clientes precisam de resposta imediata: abrir uma OS devolve o ID dela, e aprovar o orçamento já responde com o orçamento aprovado.

**Decisão**:

1. **Clientes → APIs: HTTP síncrono, estilo REST, com JSON.**
   - As APIs usam Minimal APIs do ASP.NET Core. Cada ação é uma rota, como `POST /api/ordens-servico` (OS Service) ou `POST /api/orcamentos/{ordemServicoId}/aprovar` (Management API).
   - As respostas usam os códigos HTTP para dizer o que aconteceu (201, 400, 404 etc.), como define a ADR-013.

2. **Uma única entrada: o Amazon API Gateway (HTTP API), só com HTTPS.**
   - Cada API tem um prefixo: `/management` (Management API) e `/os` (OS Service). O gateway remove o prefixo antes de encaminhar: `/management/api/...` chega à Management API como `/api/...`.
   - `POST /auth/token` vai para a Lambda que devolve o JWT (emitido pelo Auth0).
   - As APIs não ficam expostas direto na internet. O caminho até os pods passa por um VPC Link e por um balanceador interno (NLB), detalhado no [kubernetes/infra/ESTRUTURA.md](../../kubernetes/infra/ESTRUTURA.md).

3. **Autenticação na entrada e na API.** O Lambda authorizer confere o JWT (com a claim `cpf`) antes de a requisição entrar na rede do projeto, e a API confere o JWT de novo ([RFC-003](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658/blob/main/docs/rfc/RFC-003-Estrategia-de-Autenticacao.md)).

4. **Sem chamadas HTTP entre as APIs.**
   - Na fase 3, as APIs não chamavam umas às outras: compartilhavam o mesmo banco. No começo da fase 4, as 4 APIs dedicadas foram removidas, porque só repetiam rotas da Management API ([ADR-018](ADR-018-Monorepo-e-API-Unica-Management.md)).
   - Depois, o ciclo de vida da OS foi extraído para o OS Service, com banco próprio ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)). As duas APIs continuam sem chamadas HTTP entre si: trocam eventos pelo SQS (ADR-020), e nenhuma lê o banco da outra.
   - A Lambda `buscar-cliente` (repo LAMBDA) só consulta o banco da Management para conferir o CPF.

5. **Entre as Lambdas: chamada síncrona.** A Lambda que devolve o token (`issue-token`) chama a Lambda que consulta o banco (`buscar-cliente`) pela API da própria AWS Lambda (`Invoke`) e espera a resposta. A divisão em duas funções evita um NAT Gateway, que é cobrado por hora (RFC-003).

6. **Serviços externos: HTTPS síncrono.** Auth0 (emissão do token e chaves públicas), Resend (e-mail) e Datadog (o Datadog Agent envia os dados do cluster).

7. **Mensageria: superado pela ADR-020.** Na versão original desta ADR, não havia fila (Amazon SQS), tópico (Amazon SNS), barramento de eventos (Amazon EventBridge) nem Kafka: quando um passo falhava, a requisição falhava, e o cliente podia tentar de novo. Na fase 4, os serviços passaram a trocar eventos por filas Amazon SQS FIFO, numa saga coreografada ([ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e [RFC-004](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md)).

**Consequências**:
- ✅ Fácil de entender, testar (Postman) e investigar: cada pedido tem uma resposta imediata, e o trace do Datadog mostra o caminho dentro da API (ADR-014).
- ✅ Menos peças para pagar e operar: sem servidor de mensagens, sem Ingress Controller e sem balanceador público. As filas da fase 4 são do SQS, gerenciado pela AWS e dentro do nível gratuito.
- ✅ Uma só porta de entrada concentra o HTTPS, o roteamento e a autenticação, para as duas APIs.
- ✅ Sem chamadas HTTP entre os serviços: a Management API e a Lambda `buscar-cliente` usam o mesmo banco, e o OS Service fala com a Management só por eventos.
- ⚠ **Dependência em tempo real:** se o banco ou o Resend estiver fora do ar, a requisição falha na hora. Exemplo: o envio do orçamento por e-mail responde 500 se o Resend falhar (ADR-011). Entre os serviços, a fila absorve a indisponibilidade (ADR-020).
- ⚠ **Acoplamento pelo banco:** a Management API aplica as migrations ao subir (ADR-009), e uma mudança na tabela de clientes também afeta a Lambda `buscar-cliente`. O banco também é um ponto único de falha para a Management API e para a emissão do token.
- ⚠ **Latência somada:** cada requisição passa pelo Lambda authorizer, sem cache (a decisão depende da rota), e pela rede interna até o pod.
- ⚠ **Operações sem transação única:** algumas operações gravam em etapas separadas. A abertura completa da OS (`/completa`), que gravava cliente, veículo, peças, serviços e OS em etapas, foi removida na fase 4 (ADR-019). Continuam assim o débito de estoque (ADR-012) e a publicação do evento depois de gravar (ADR-020).

**Alternativas Rejeitadas**:
- Mensageria (SQS, SNS ou EventBridge), por exemplo para o e-mail:
  - rejeitada por somar serviços, filas de erro e processamento em segundo plano que o escopo daquela fase não exigia.
  - **Adotada na fase 4** para a comunicação entre os serviços, com o SQS FIFO ([ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e [RFC-004](../rfc/RFC-004-Comunicacao-entre-Microsservicos.md)).
- Chamadas HTTP entre APIs, cada uma com o próprio banco (*database per service*):
  - rejeitada na fase 3, quando havia várias APIs, por exigir mais instâncias de banco (custo), cópia e sincronização de dados e tratamento de falhas em cada chamada.
  - Na fase 4, o banco por serviço foi adotado para o OS Service (ADR-019), mas as chamadas HTTP entre as APIs continuam rejeitadas: a comunicação é por eventos (RFC-004).
- gRPC:
  - rejeitada porque não há chamadas síncronas entre serviços, e os clientes (collections do Postman) usam REST com JSON.
- Ingress Controller no cluster, a solução usada antes:
  - substituída pelo API Gateway, que oferece HTTPS sem domínio nem certificado próprios e permite o Lambda authorizer na entrada.
- Um balanceador público para a API (Service do tipo `LoadBalancer`):
  - rejeitada pelo custo por hora do balanceador e por expor a API sem a autenticação da entrada.

**Relação com Outras ADRs**:
- **ADR-009**: cada API aplica as próprias migrations ao subir, mantendo o banco no formato que o código espera.
- **ADR-011**: o envio do orçamento por e-mail é síncrono; o e-mail de status sai do consumidor do evento.
- **ADR-013**: os erros esperados viram respostas HTTP claras, parte do contrato REST.
- **ADR-014**: logs e traces acompanham cada requisição síncrona dentro de cada API.
- **ADR-016**: como as APIs não guardam estado, elas escalam criando mais pods: [ADR-016](ADR-016-Escalabilidade-com-HPA.md).
- **ADR-017**: o banco PostgreSQL no Amazon RDS: [ADR-017](ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md).
- **ADR-018**: no começo da fase 4, as 4 APIs dedicadas foram removidas e ficou só a Management API neste monorepo: [ADR-018](ADR-018-Monorepo-e-API-Unica-Management.md).
- **ADR-019**: o OS Service, com banco próprio (repo OS-SERVICE): [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md).
- **ADR-020**: eventos no SQS e saga coreografada entre os serviços: [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md).
