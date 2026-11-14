# ADR-016: Escalabilidade da API com HPA

**Status**: Aceita

**Resumo**:
A Management API tem um **HPA** (Horizontal Pod Autoscaler): uma regra do Kubernetes que aumenta ou diminui a quantidade de cópias (pods) da API conforme o uso de CPU e memória, de 1 a 10 pods. A quantidade de máquinas (nós) do cluster não muda: são sempre 2.

> As ADRs são numeradas em sequência única entre este monorepo e o repositório [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice de todas as decisões está no [docs/arquitetura.md](../arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

**Contexto**:
- A Management API roda no Amazon EKS, em um Deployment (`management-api`) que começa com 1 pod. Desde a [ADR-018](ADR-018-Monorepo-e-API-Unica-Management.md), ela é a única API do sistema.
- Cada pod **reserva** 100m de CPU (um décimo de um núcleo) e 128Mi de memória (o *request*) e pode **usar no máximo** 500m de CPU e 512Mi de memória (o *limit*).
- A carga muda ao longo do dia. É preciso aguentar picos sem deixar pods sobrando no resto do tempo.
- O cluster é pequeno e tem custo fixo: 2 nós `t3.small` (mínimo, desejado e máximo iguais a 2), com espaço para 22 pods e cerca de 1,4 GiB de memória livre para pods em cada nó ([k8s/README.md](../../kubernetes/k8s/README.md#61-capacidade-do-cluster), seção 6.1).
- O requisito do projeto para o autoscaling é: CPU-alvo de 30%, mínimo de 1 e máximo de 10 réplicas ([k8s/README.md](../../kubernetes/k8s/README.md#6-regras-de-hpa-aplicadas), seção 6).

**Decisão**:

1. **Um HPA para a API** (`autoscaling/v2`), no arquivo `kubernetes/k8s/features/management-api/hpa.yml`, com estas regras:
   - mínimo de 1 e máximo de 10 pods;
   - CPU-alvo de 30% do request, ou seja, 30m por pod, em média;
   - memória-alvo de 150% do request, ou seja, 192Mi por pod, em média. O alvo era 80% e subiu porque o tracer do Datadog aumenta a memória de cada pod ([ADR-014](ADR-014-Observabilidade-com-Datadog.md)).
2. **As medições vêm do Metrics Server**, instalado por Terraform em `kubernetes/infra/addons` ([metrics-server.tf](../../kubernetes/infra/addons/metrics-server.tf)).
3. **Escala horizontal:** o HPA cria mais pods iguais, em vez de deixar cada pod maior. Isso funciona porque a API não guarda dados na memória entre um pedido e outro: tudo fica no PostgreSQL. Assim, qualquer pod atende qualquer pedido, e o Service do Kubernetes divide os pedidos entre eles.
4. **Nós fixos:** o cluster não cria máquinas novas sozinho (sem Cluster Autoscaler ou Karpenter). O node group fica em 2 nós para o custo não crescer sem controle.

Como o HPA decide, em poucas palavras:

```mermaid
flowchart LR
  Pods[Pods da API] -->|uso de CPU e memoria| MS[Metrics Server]
  MS -->|media dos pods| HPA[HPA da API]
  HPA -->|acima do alvo: mais pods<br/>abaixo do alvo: menos pods| Pods
```

- A cada 15 segundos, o HPA compara o uso médio dos pods com o alvo.
- Se a média passa do alvo, ele calcula quantos pods trariam o uso de volta ao alvo e cria os que faltam, até 10. Exemplo: 1 pod usando 60m de CPU está no dobro do alvo (30m), então o HPA sobe para 2 pods.
- Quando o uso cai, ele remove pods aos poucos, depois de 5 minutos de calma, até voltar a 1.
- Com duas métricas, vale a que pedir mais pods.

**Consequências**:
- ✅ A API cresce sozinha quando recebe mais pedidos e volta a 1 pod quando a carga passa, sem ação manual.
- ✅ Custo previsível: como os nós são fixos, o HPA só usa o espaço que já existe no cluster.
- ⚠ **O limite real é o espaço nos 2 nós.** O cluster comporta 22 pods no total e, em uso normal, já roda 11 (a API, o Datadog e os pods do próprio Kubernetes). No máximo de 10 pods da API, sobra pouco espaço, principalmente de memória, e pods além da capacidade ficam `Pending` (esperando espaço). A saída prevista é trocar os nós por `t3.medium` ([k8s/README.md](../../kubernetes/k8s/README.md#61-capacidade-do-cluster), seção 6.1).
- ⚠ **CPU-alvo baixo (30%):** o HPA escala cedo. Responde rápido aos picos, mas pode criar pods com pouca carga.
- ⚠ **Memória como métrica:** a memória de uma API .NET demora a cair depois de um pico e pode segurar pods a mais por um tempo. Se o HPA escalar sem carga, a orientação é deixar só a CPU ([k8s/README.md](../../kubernetes/k8s/README.md#6-regras-de-hpa-aplicadas), seção 6).
- ⚠ **Escalar não é instantâneo:** um pod novo precisa baixar a imagem, iniciar a API e passar no teste de saúde (`/health`) antes de receber pedidos.
- ⚠ **Depende do Metrics Server:** sem ele, o HPA fica sem medições e para de escalar (os pods atuais continuam rodando).

**Alternativas Rejeitadas**:
- Quantidade fixa de pods, sem autoscaling:
  - rejeitada porque sobra recurso fora dos picos e falta nos picos.
- VPA (Vertical Pod Autoscaler), que aumenta o tamanho de cada pod em vez da quantidade:
  - rejeitada porque precisa recriar os pods para mudar o tamanho, não ajuda em picos rápidos e exigiria instalar mais componentes no cluster.
- Cluster Autoscaler ou Karpenter, que criam e removem nós:
  - rejeitada para manter o custo fixo e previsível num projeto de estudo.
- KEDA, que escala por eventos (como o tamanho de uma fila):
  - rejeitada porque o sistema não tem filas nem eventos para medir ([ADR-015](ADR-015-Comunicacao-Sincrona-via-API-Gateway.md)).
- Métricas personalizadas, como pedidos por segundo vindos do Datadog:
  - rejeitada porque exigiria um adaptador de métricas no cluster; CPU e memória bastam para o escopo.

**Relação com Outras ADRs**:
- **ADR-014**: o tracer do Datadog levou o alvo de memória de 80% para 150% e reduziu a folga do cluster.
- **ADR-015**: a comunicação síncrona, sem filas e com a API sem estado, é o que permite escalar só criando mais pods.
- **ADR-018**: o monorepo e a API única deixaram um só Deployment e um só HPA ([ADR-018](ADR-018-Monorepo-e-API-Unica-Management.md)).
- **RFC-001**: a escolha da AWS e dos nós `t3.small` define a capacidade do cluster ([RFC-001](../rfc/RFC-001-Escolha-da-Nuvem.md)).
