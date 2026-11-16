# Observabilidade com Datadog

O `kubernetes/infra/addons` instala o Datadog Agent no EKS e cria, num módulo local, os recursos do Datadog: métricas de log, monitores, synthetics, SLO e dashboard. Tudo fica em Terraform, no mesmo root e no mesmo state do Metrics Server (`techchallenge-oficina/fase-4/k8s-addons.tfstate`), e roda pelas pipelines que já existem, sem job novo.

A parte da aplicação (logs JSON, eventos de negócio e `/health/ready`) fica nas APIs: a Management API (`server/`, na raiz do monorepo) e o OS Service ([repo OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658)), que segue o mesmo contrato. O contrato entre as partes está em [docs/observabilidade.md](../../docs/observabilidade.md), e a decisão, na [ADR-014](../../docs/adr/ADR-014-Observabilidade-com-Datadog.md). O repo OS-SERVICE não tem Terraform do Datadog: tudo fica aqui.

| # | Requisito | Como é atendido |
|---|---|---|
| R1 | Latência das APIs | APM: tracer .NET injetado no cluster → p95 de `trace.aspnet_core.request` por serviço (`management-api` e `os-service-api`), no monitor e no dashboard; por rota, na página do serviço no APM |
| R2 | Recursos do Kubernetes | Agent (DaemonSet + Cluster Agent) → métricas `kubernetes.*` e `kubernetes_state.*`; monitores de CPU e memória contra o limit, restarts, CrashLoopBackOff e nodes; widgets. Os monitores e widgets por deployment filtram o namespace `oficina` |
| R3 | Healthchecks e uptime | `http_check` do Agent no `/health/ready` de cada API, que checa o banco; Synthetics em `GET /management/health` e `GET /os/health` pelo API Gateway, com SLO |
| R4 | Falhas no processamento de OS | `os.falha` por etapa, 5xx por rota (APM), `integracao.falha` (Resend e SQS) e banco indisponível; métricas e monitores |
| R5 | Logs estruturados | Um JSON por linha no stdout, coletado pelo Agent |
| R6 | Correlação | `dd.trace_id` e `dd.span_id` nos logs ligam cada log ao trace do request, dentro da API. API Gateway e Lambdas não são rastreados |
| R7 | Dashboard | Um dashboard com OS, APIs, Kubernetes, disponibilidade e logs de erro |

## 1. O que é criado

### 1.1 Datadog Agent (Helm)

O [datadog-agent.tf](addons/datadog-agent.tf) instala o chart `datadog/datadog` `3.255.0` (`helm_release.datadog`, repositório `https://helm.datadoghq.com`) no namespace `datadog`, com os valores de [datadog-agent-values.yaml](addons/datadog-agent-values.yaml):

| Item | Configuração |
|---|---|
| Pods | Um Agent por node (DaemonSet, 2 pods) e um Cluster Agent. Operator e system-probe ficam desligados, o que economiza um pod e um container. PriorityClass própria |
| Recursos | Agent 100m/256Mi (limit 512Mi), trace-agent 50m/128Mi (limit 256Mi) e Cluster Agent 50m/128Mi (limit 384Mi) |
| Cluster e tags | `clusterName: techchallenge-oficina-eks`, explícito porque o IMDSv2 dos nodes impede a autodetecção, e a tag `env:study` |
| APM .NET | Single Step Instrumentation só nos namespaces `oficina` (Management API) e `os-service` (OS Service), com o tracer .NET v3, `DD_LOGS_INJECTION=true` e `DD_RUNTIME_METRICS_ENABLED=true`. O admission controller injeta nos pods o init container `datadog-lib-dotnet-init` e as envs `DD_*`. A imagem e o Dockerfile não mudam |
| Probes fora do APM | `DD_APM_IGNORE_RESOURCES=^GET /health` |
| Logs | Stdout de todos os containers, exceto dos namespaces `kube-system` e `datadog` |
| `http_check` | Cluster check com duas instâncias (timeout de 5 s e tag `env:study`): `management-api`, em `http://management-api.oficina.svc.cluster.local/health/ready`, com a tag `service:management-api`, e `os-service-api`, em `http://os-service-api.os-service.svc.cluster.local/health/ready`, com a tag `service:os-service-api`. Gera o service check `http.can_connect` |

O `service` e o `env` de cada pod vêm dos labels `tags.datadoghq.com/*` do Deployment ([k8s/README.md](../k8s/README.md#44-labels-do-datadog-unified-service-tagging); o do OS Service fica no repo OS-SERVICE). Só os pods criados com o Agent já rodando recebem o tracer: por isso o Bootstrap e o Deploy aplicam o addons antes dos manifests, com restart. O OS Service sobe depois, no Bootstrap do repo dele, com o Agent já rodando.

### 1.2 Recursos no Datadog (módulo `addons/modules/datadog/`)

O [datadog.tf](addons/datadog.tf) chama o módulo local [addons/modules/datadog/](addons/modules/datadog):

| Arquivo | Recursos |
|---|---|
| `logs-metrics.tf` | 5 métricas de log (seção 5.4) |
| `monitors.tf` | 10 monitores (tabela abaixo) |
| `synthetics.tf` | 2 testes de API, um por entrada de `services` em `locals.tf` (`management` → `management-api` e `os` → `os-service-api`): `GET <endpoint do API Gateway>/management/health` e `GET <endpoint do API Gateway>/os/health`, com status 200 e resposta em menos de 2000 ms, a cada 5 min, a partir de `aws:us-east-1`. A URL do API Gateway `techchallenge-oficina-api` é lida por data source, porque muda a cada Bootstrap. Mais 1 SLO de uptime (99% em 7 dias) sobre os dois testes |
| `dashboard.tf` | 1 dashboard, com template variable `env`, nos grupos Ordens de Serviço, APIs, Kubernetes, Disponibilidade e Logs de erro recentes |

| Requisito | Monitor | Condição |
|---|---|---|
| R1 | Latência p95 por serviço | `trace.aspnet_core.request{env:study} by {service}` acima de 1 s (warning em 0,5 s) |
| R2 | CPU | Uso acima de 80% do limit |
| R2 | Memória | `working_set` acima de 85% do limit |
| R2 | CrashLoopBackOff | Container em CrashLoopBackOff |
| R2 | Restarts | Mais de 2 restarts em 15 min por deployment no namespace `oficina` |
| R2 | Nodes | Menos de 2 nodes Ready. É o único que também alerta na ausência de dados |
| R3 | Réplicas indisponíveis | `kubernetes_state.deployment.replicas_unavailable` ≥ 1 durante 15 min seguidos. Um pod subindo num scale-out do HPA não alerta |
| R3 | Health interno | `http.can_connect` por `service`, nas últimas 3 execuções |
| R4 | 5xx por rota | `trace.aspnet_core.request.errors{env:study} by {service,resource_name}` > 0 |
| R4 | Erros de integração | `oficina.integracao.erros` > 0 em 15 min |

- As falhas de regra de negócio (`os.falha`) ficam só no dashboard, sem monitor: o e2e (`e2e/` e a collection do repo OS-SERVICE) gera erros 400 de propósito.
- Os monitores de APM (latência e 5xx), o de `http.can_connect` e o de erros de integração cobrem as duas APIs. O de erros de integração também dispara com `integracao:sqs`.
- **Limitação:** os monitores e os widgets de Kubernetes por deployment (CPU, memória, CrashLoopBackOff, restarts e réplicas indisponíveis) filtram o namespace `oficina`. Os pods do OS Service (namespace `os-service`) ficam de fora deles; o OS Service aparece no APM, nos logs, no `http.can_connect` e no synthetic.
- Os monitores têm o prefixo `[oficina]` no nome e a tag `env:study`, e notificam `DD_ALERT_RECIPIENTS`. Sem esse secret, os alertas aparecem só no Datadog.
- O output `datadog_dashboard_url` do addons traz a URL do dashboard e aparece no fim do apply (Bootstrap ou Deploy).

Ficam fora do Terraform:
- a conta e as chaves, que são a credencial do próprio provider;
- facets: não existe resource para elas, e as métricas e os monitores usam atributos;
- a quota diária do índice de logs: opcional, na UI.

## 2. Configuração

### 2.1 Conta e chaves (manual, uma vez)

1. Crie a conta **trial** em [datadoghq.com/free-datadog-trial](https://www.datadoghq.com/free-datadog-trial/), **sem cartão**.
   - Faça isso **só quando for subir o ambiente e demonstrar**: os 14 dias contam do cadastro.
   - Mantenha o site padrão US1 (`datadoghq.com`) ou anote o escolhido, que aparece na URL depois do login.
2. Organization Settings → **API Keys** → New Key → copie.
3. Organization Settings → **Application Keys** → New Key (**não-escopada**) → copie na hora, porque ela só aparece uma vez.
4. Cadastre os secrets e as variables da seção 2.2 no repositório do monorepo.
5. Opcional: Logs → Indexes → `main` → daily quota.

A aplicação, o `DB/`, o `SQS/` e os repos OS-SERVICE e LAMBDA não têm nada a configurar.

### 2.2 Secrets e Variables do GitHub

Em **Settings → Secrets and variables → Actions**, no nível do **repositório**. Os jobs do Terraform não usam environment, então secrets de environment não chegam a eles.

| Nome | Tipo | Valor | Entra no Terraform como |
|---|---|---|---|
| `DD_API_KEY` | Secret | API Key | `TF_VAR_datadog_api_key` |
| `DD_APP_KEY` | Secret | Application Key (não-escopada) | `TF_VAR_datadog_app_key` |
| `DD_ALERT_RECIPIENTS` | Secret (opcional) | Destinatários dos monitores, ex.: `@seu-email@dominio.com`. É secret porque o repositório é público e o plan imprime as mensagens dos monitores | `TF_VAR_datadog_alert_recipients` |
| `DD_SITE` | Variable | `datadoghq.com` (US1) ou o site escolhido, ex.: `us5.datadoghq.com`. Só o host, sem `https://` e sem `app.` | `TF_VAR_datadog_site` |
| `DD_ENABLED` | Variable (opcional) | `true` (padrão) / `false` para desligar o Datadog (seção 3) | `TF_VAR_datadog_enabled` |
| `DD_SYNTHETICS_STATUS` | Variable (opcional) | `live` (padrão) / `paused` | `TF_VAR_datadog_synthetics_status` |

Sites aceitos: `datadoghq.com`, `us3.datadoghq.com`, `us5.datadoghq.com`, `datadoghq.eu`, `ap1.datadoghq.com` e `ap2.datadoghq.com`.

No [_terraform.yml](../../.github/workflows/_terraform.yml):
- O passo **Conferir secrets e variables do Datadog** roda no `kubernetes/infra/addons`, no plan, no apply e no destroy. Ele falha se `DD_API_KEY`, `DD_APP_KEY` ou `DD_SITE` estiver vazio e, com `DD_ENABLED=false`, só emite um aviso.
- Os passos de plan e de apply/destroy recebem os valores como `TF_VAR_datadog_*`. A foundation e o `DB/infra` os ignoram, porque não declaram essas variáveis.
- Sem a variable `DD_SITE` (o que só passa pela conferência com `DD_ENABLED=false`), o `TF_VAR_datadog_site` vai como `datadoghq.com`, o padrão do `variables.tf`. Vazio, ele apontaria o provider para uma URL inválida.
- O `validate` dos PRs roda sem as chaves.

Localmente, as chaves vão por `TF_VAR_datadog_*`, e `datadog_enabled`, `datadog_site` e `datadog_synthetics_status` ficam no `terraform.tfvars`, com os mesmos valores do GitHub (passo a passo em [README.md](README.md#4-instale-os-addons)).

**Segurança:**
- As chaves e os destinatários são variáveis `sensitive` e não aparecem no plan nem nos logs.
- A API Key chega ao Agent por `set_sensitive`. Ela fica no state do addons (bucket privado e criptografado) e num Secret do namespace `datadog`.
- A Application Key não é escopada: tem as permissões do usuário que a criou. Revogue as duas chaves ao fim do estudo.
- Os logs não trazem CPF/CNPJ, e-mail, nome, placa nem JWT, só GUIDs. Os ids são atributos de log, nunca tags de métrica.

## 3. Liga/desliga (`DD_ENABLED`)

| Valor | Efeito no próximo apply do addons |
|---|---|
| `true` (padrão, também com a variable ausente) | Instala ou atualiza o Agent e cria ou atualiza os recursos do Datadog. Exige `DD_API_KEY`, `DD_APP_KEY` e `DD_SITE` |
| `false` | `count = 0` no `helm_release.datadog` e no `module.datadog`: o Agent é desinstalado e os recursos do Datadog são apagados. O provider deixa de validar as chaves, mas apagar recursos que já existem ainda exige as chaves e o site da conta: mantenha `DD_API_KEY`, `DD_APP_KEY` e `DD_SITE` válidos até o apply que desliga o Datadog terminar |

- Trocar a variable não dispara nenhum workflow. Para aplicar, rode o **Bootstrap**, que reaplica tudo a partir da `main` (foundation, addons, RDS, imagem e manifests); um push em `kubernetes/infra/**` (Deploy) também aplica. O mesmo vale para `DD_SYNTHETICS_STATUS`.
- O tracer só entra ou sai quando os pods são recriados. O Bootstrap e o Deploy aplicam o addons e terminam com o apply dos manifests com restart, então os pods já sobem com (ou sem) o tracer, ao ligar (`DD_ENABLED` de `false` para `true`, inclusive depois de começar sem conta) ou ao desligar. Se o apply dos manifests falhar ou não rodar, rode o **K8s Apply** com `restart-pods = true`. Sem o restart, os pods continuam como estavam: ao ligar, faltam o APM (R1) e o `dd.trace_id` nos logs (R6); ao desligar, os pods já instrumentados continuam com o tracer.
- O Bootstrap e o Deploy deste monorepo só reiniciam os pods da Management API. Depois de ligar ou desligar o Datadog, rode também o **K8s Apply do repo OS-SERVICE** com `restart-pods = true`, para os pods do OS Service subirem com (ou sem) o tracer.
- Ao desligar com o cluster de pé, ficam dois restos que o Helm não remove: o namespace `datadog` (criado pelo `create_namespace`) e, provavelmente, o `MutatingWebhookConfiguration` `datadog-webhook`, que o Cluster Agent cria em tempo de execução. O webhook usa `failurePolicy: Ignore` e não bloqueia a criação de pods. Para limpar: `kubectl delete mutatingwebhookconfiguration datadog-webhook --ignore-not-found` e `kubectl delete namespace datadog`. No Destroy, os dois somem junto com o cluster.
- Com `DD_ENABLED=false` desde o início, o addons sobe só com o Metrics Server, e nenhuma chave é necessária.

## 4. Trial de 14 dias

- Durante o trial, a plataforma inteira fica liberada (APM, logs e synthetics), sem custo e sem cartão. O synthetic a cada 5 min (~288 execuções por dia) está coberto.
- Ao fim do trial, a org cai para o plano Free: só infraestrutura (até 5 hosts, retenção de 1 dia), **sem APM, logs e synthetics**. Os widgets de negócio, que dependem das métricas de log, param de atualizar.
- A demonstração e a gravação precisam acontecer dentro da janela.
- **Antes de o trial acabar** (ou ao fim do estudo): defina `DD_ENABLED=false` e rode o Bootstrap (seção 3), ou rode o **Destroy** do ambiente.
- **Nunca cadastre cartão.** O plano pago custaria cerca de US$ 110–150/mês, porque a cobrança dos hosts (Infra + APM) não é proporcional às horas ligadas.

## 5. Contrato com a aplicação (espelho)

A fonte da verdade é o [docs/observabilidade.md](../../docs/observabilidade.md), na raiz do monorepo. As métricas, os monitores e o dashboard filtram valores exatos: uma mudança em nome de evento, atributo ou valor precisa ser feita na aplicação (`server/` e o repo OS-SERVICE, que tem a própria cópia do catálogo) e no módulo `addons/modules/datadog/`.

### 5.1 Formato do log

Um JSON por linha no stdout, com estas chaves no primeiro nível:
- `timestamp`, `level`, `message` e `evento` (nome do `EventId`). O `level` (`Information`, `Warning`, `Error` etc.) vira o status do log no Datadog.
- `logger.name`: a categoria do logger.
- `dd.trace_id`, `dd.span_id`, `dd.service`, `dd.env` e `dd.version`: só com o tracer, ou seja, no cluster. O `dd.trace_id` liga o log ao trace.
- `error.kind`, `error.message` e `error.stack`: só em log com exceção.
- As propriedades do evento, em PascalCase (seção 5.2).

O `evento` também traz nomes de eventos do framework; por isso as métricas filtram valores exatos.

### 5.2 Eventos

| EventId | `evento` | `level` | Serviço | Propriedades |
|---|---|---|---|---|
| 1001 | `os.criada` | Information | `os-service-api` | `OrdemServicoId`, `ClienteId`, `VeiculoId`, `QuantidadeServicos` |
| 1002 | `os.status_alterado` | Information | `os-service-api` | `OrdemServicoId`, `StatusAnterior`, `StatusNovo`, `Etapa`, `TempoNoStatusAnteriorSegundos` (número) |
| 1003 | `os.encerrada` | Information | `os-service-api` | `OrdemServicoId`, `TempoTotalSegundos` (número). A OS é encerrada na entrega (status Entregue) |
| 1004 | `os.orcamento_gerado` | Information | `management-api` | `OrdemServicoId`, `ValorTotal`, `Regerado` |
| 1005 | `os.orcamento_enviado` | Information | `management-api` | `OrdemServicoId`, `ValorTotal` |
| 2001 | `os.falha` | Warning | Os dois | `Etapa`, `OrdemServicoId`, `TipoFalha`, `Motivo` |
| 3001 | `integracao.falha` | Error (envio do orçamento e SQS) / Warning (notificação de status) | Os dois (Resend só na Management) | `Integracao`, `Operacao`, `OrdemServicoId`; na notificação, também `StatusNovo`; no SQS, também `Tipo` e, no consumo, `EventoId` e `MessageId` (`MensagemId` no OS Service) |
| 4001 | `evento.publicado` | Information | Os dois | `Tipo`, `EventoId`, `OrdemServicoId`, `Fila` |
| 4002 | `evento.consumido` | Information | Os dois | `Tipo`, `EventoId`, `Fila` |
| 4003 | `evento.ignorado` | Information (Management) / Warning (OS Service) | Os dois | `Tipo`, `EventoId`, `Fila` |
| 9001 | `http.erro_nao_tratado` | Error | Os dois | `RequestMethod`, `RouteTemplate`; `Integracao=postgres` só com o banco indisponível |
| 9002 | `app.migracao_falhou` | Fatal (`LogCritical`) | Os dois | Exceção em `error.*` |

### 5.3 Valores estáveis

| Atributo | Valores |
|---|---|
| `Etapa` | `abertura`, `atualizacao`, `exclusao`, `diagnostico`, `orcamento_geracao`, `orcamento_envio`, `orcamento_aprovacao`, `orcamento_recusa`, `execucao`, `finalizacao`, `entrega`, `cancelamento` |
| `TipoFalha` | `regra_negocio` (DomainException), `nao_encontrado` (KeyNotFoundException) |
| `StatusAnterior` / `StatusNovo` | `recebida`, `em_diagnostico`, `aguardando_aprovacao`, `em_execucao`, `finalizada`, `entregue`, `encerrada`, `cancelada` |
| `Integracao` | `resend`, `postgres`, `sqs` |
| `Operacao` | `envio_orcamento`, `notificacao_status`, `publicacao`, `consumo` |

### 5.4 Métricas de log

| Métrica | Tipo | Filtro | Tags (group_by) |
|---|---|---|---|
| `oficina.os.criadas` | count | `@evento:os.criada` | `env` |
| `oficina.os.encerradas` | count | `@evento:os.encerrada` | `env` |
| `oficina.os.tempo_no_status` | distribution de `@TempoNoStatusAnteriorSegundos` | `@evento:os.status_alterado` | `status_anterior` (de `@StatusAnterior`), `env` |
| `oficina.os.falhas` | count | `@evento:os.falha` | `etapa` (de `@Etapa`), `tipo_falha` (de `@TipoFalha`), `env` |
| `oficina.integracao.erros` | count | `@Integracao:*` | `integracao` (de `@Integracao`), `env` |

Métricas de log não têm backfill: contam só os logs recebidos depois de criadas.

### 5.5 Serviços e tags

- `service`: o nome do Deployment (`management-api` e, no repo OS-SERVICE, `os-service-api`), pelo label `tags.datadoghq.com/service`.
- `env`: `study`, pelo label `tags.datadoghq.com/env` e pela tag global do Agent. É o mesmo `environment` da foundation.
- Synthetics: prefixos do API Gateway `management` (`GET /management/health`) e `os` (`GET /os/health`), mapeados para os serviços em `services`, no `locals.tf` do módulo.
- As métricas de log não filtram por `service`: somam os eventos das duas APIs.

## 6. Primeira ativação

1. Configure a conta, as chaves e os secrets e variables (seção 2).
2. Rode o **Bootstrap** (ou, com o ambiente de pé, faça o merge de uma mudança em `kubernetes/infra/**`). O addons instala o Agent e cria os recursos do Datadog antes do apply dos manifests com restart, então os pods recriados sobem instrumentados.

A Management API e o Datadog ficam no mesmo repositório, então não há ordem de merge entre eles: o `/health/ready`, usado pelo `http_check`, já está na Management API e no OS Service. Até o Bootstrap do repo OS-SERVICE, o `http.can_connect` e o synthetic do `os-service-api` ficam vermelhos.

## 7. Verificação depois do deploy

Dentro dos 14 dias do trial:

**Cluster:**
- `kubectl get pods -n datadog`: 2 agents e 1 cluster-agent `Running`.
- `kubectl describe pod -n oficina <pod>` e `kubectl describe pod -n os-service <pod>`: init container `datadog-lib-dotnet-init` e envs `DD_*`.
- `kubectl top pods -n oficina` e `kubectl get hpa -n oficina` (e o mesmo em `-n os-service`): sem scale-out espúrio. Se o HPA escalar sem carga, ajuste-o ([k8s/README.md](../k8s/README.md#6-regras-de-hpa-aplicadas)).

**No Datadog:**
- Infrastructure → Kubernetes: cluster, nodes e pods (R2).
- APM: os serviços `management-api` e `os-service-api`, com latência por rota (R1).
- Logs: status, `service` e `evento` preenchidos, e a aba **Trace** ligada ao request (R5/R6). Consultas úteis: `@evento:os.falha`, `@Integracao:*` e `@evento:evento.consumido`.
- Synthetics verdes, SLO e `http.can_connect` do `management-api` e do `os-service-api` (R3).

**Dashboard (R7/R4)**, pela URL do output `datadog_dashboard_url`:
- rode a collection do repo OS-SERVICE, com o fluxo completo da saga: criar a OS → gerar o orçamento na Management → aprovar → finalizar → entregar;
- confira os widgets preenchidos (as métricas de log não têm backfill);
- aprovar o mesmo orçamento duas vezes gera `os.falha` na etapa `orcamento_aprovacao`, na Management;
- uma chave do Resend inválida no envio do orçamento gera `integracao.falha` (`resend`) e um 5xx na rota.

**Alertas de latência logo depois de um restart:** o primeiro request de cada pod recriado (Bootstrap, Deploy ou K8s Apply com restart) é lento (JIT, tracer, inicialização do EF Core e primeira conexão com o banco), muitas vezes acima de 1 s. Como os probes em `/health` ficam fora do APM, com pouco tráfego esse request sozinho vira o p95 da janela de 5 min do monitor de latência (R1), que pode avisar ou alertar. É esperado: o monitor volta ao normal quando os requests seguintes, já rápidos, ocupam a janela. Latência alta de verdade persiste com tráfego contínuo.

Ao fim do trial ou do estudo: `DD_ENABLED=false` e Bootstrap (seção 3), ou o Destroy.

## 8. Troubleshooting

- **O passo "Conferir secrets e variables do Datadog" falha:** cadastre `DD_API_KEY`, `DD_APP_KEY` e `DD_SITE` no nível do repositório (seção 2.2), ou defina `DD_ENABLED=false` para subir sem o Datadog.
- **Agent ou pods `Pending`** (`kubectl describe pod` mostra `Too many pods` ou `Insufficient memory`):
  - Os 2 nodes `t3.small` comportam 22 pods. Com o Datadog e as duas APIs, são 12 em regime e cerca de 13 durante o rollout restart de uma API, mais as réplicas extras dos HPAs ([k8s/README.md](../k8s/README.md#61-capacidade-do-cluster)).
  - Causas comuns: réplicas extras dos HPAs (`kubectl get hpa -A`) ou um rollout em andamento. Aguarde o rollout e, se um HPA estiver escalando sem carga, ajuste-o.
  - Se os pods do Agent não ficarem prontos em 600 s, o apply do addons falha por timeout do Helm. Depois de liberar espaço, rode o Bootstrap de novo.
  - Contingência: trocar os nodes para `t3.medium` (`node_instance_type` da foundation). Isso **substitui o node group** e derruba os pods durante a troca: faça só numa janela, ou com Destroy e Bootstrap.
- **Pods sem traces no APM**, sem o init container `datadog-lib-dotnet-init`: foram criados antes do Agent. Rode o K8s Apply com `restart-pods = true` no repositório da API afetada (este monorepo ou o OS-SERVICE).
- **`http.can_connect` em CRITICAL com 404:** a imagem em execução não tem o `/health/ready`. Republique a imagem da `main` com o Bootstrap, que também reinicia os pods.
- **Synthetic vermelho durante o Bootstrap:** é esperado. O Bootstrap cria o API Gateway e o teste (foundation e addons), mas a API só sobe no fim, depois do RDS e da imagem. Para não gastar o SLO, defina `DD_SYNTHETICS_STATUS=paused` antes do Bootstrap e volte para `live` depois, rodando o Bootstrap de novo.
- **Widgets de negócio vazios:** as métricas de log só contam eventos recebidos depois de criadas. Gere tráfego (e2e em `e2e/`). Depois do trial, elas param de atualizar (seção 4).
- **Chaves inválidas ou revogadas** (o plan, o apply ou o destroy do addons falha com `403 Forbidden` nos recursos `datadog_*`):
  1. Gere chaves novas na org (Organization Settings), atualize `DD_API_KEY` e `DD_APP_KEY` e rode de novo.
  2. `DD_ENABLED=false` evita a validação das chaves, mas só resolve se o state do addons não tiver os recursos do `module.datadog` (por exemplo, com o ambiente recriado do zero depois de um Destroy completo).
  3. Último recurso, sem chaves válidas: tire o Datadog do state, localmente, em `kubernetes/infra/addons`, com o usuário `terraform` e sem workflow rodando. Os dois usam `count`, por isso o `[0]`:
     ```powershell
     terraform init
     terraform state rm 'module.datadog[0]'
     terraform state rm 'helm_release.datadog[0]'
     ```
     - Com o cluster de pé, pule o segundo comando: o `helm_release.datadog[0]` não depende das chaves, e o apply com `DD_ENABLED=false` desinstala o Agent. Fora do state, o Agent ficaria no cluster sem o Terraform.
     - Os recursos removidos do state continuam na org do Datadog. Apague-os pela UI, se ainda tiver acesso.
     - Depois, com `DD_ENABLED=false` (ou chaves válidas), rode de novo o que falhou: o Bootstrap (ou o Re-run do Deploy, se for o run mais recente da `main`) ou o Destroy. Se o Deploy pulou o `apply-k8s` por causa da falha, rode o **K8s Apply** manualmente.
- **Destroy vermelho no `terraform-destroy-addons`:** a foundation é destruída mesmo assim, para o EKS não ficar ligado gerando custo. O state do addons fica com o que não foi destruído. Resolva logo, mesmo que não vá subir o ambiente de novo:
  - Os recursos do Datadog que sobraram continuam ativos: o synthetic segue testando, a cada 5 min, um API Gateway que não existe mais, o monitor de nodes alerta por falta de dados depois de 30 min, e o SLO consome o error budget.
  - Sem o cluster, o addons não roda mais: o provider Helm e o output `cluster_name` leem o `data.aws_eks_cluster`, que falha. Todo Destroy seguinte fica vermelho no addons, e pausar os synthetics com `DD_SYNTHETICS_STATUS` não adianta, porque o apply do addons também falha.
  - Saída 1, com as chaves e o `DD_SITE` válidos (gere chaves novas, se preciso): rode o **Bootstrap**, que recria o cluster e reaplica o addons, e depois o **Destroy** de novo, que apaga os recursos do Datadog junto com o resto.
  - Saída 2, sem subir o ambiente: tire o `module.datadog[0]` do state (primeiro `state rm` do item anterior) e apague pela UI do Datadog os monitores, o synthetic, o SLO e o dashboard com prefixo `[oficina]`, e as métricas de log `oficina.*` (Logs → Generate Metrics).
