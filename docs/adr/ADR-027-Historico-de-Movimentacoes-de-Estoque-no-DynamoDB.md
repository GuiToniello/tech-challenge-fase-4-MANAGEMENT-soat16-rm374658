# ADR-027: Histórico de Movimentações de Estoque no Amazon DynamoDB

**Status**: Aceita (fase 4). Complementa a [ADR-012](ADR-012-Controle-de-Estoque-de-Insumos.md), em que o saldo do estoque continua no PostgreSQL, e segue o padrão do histórico de pagamentos do Billing ([ADR-024](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-024-Historico-de-Pagamentos-no-DynamoDB.md)), da linha do tempo da produção do Execução e Produção ([ADR-025](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-025-Linha-do-Tempo-da-Producao-no-DynamoDB.md)) e do histórico da saga do OS Service ([ADR-026](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-026-Historico-da-Saga-no-DynamoDB.md))

**Resumo**:
A Management passa a guardar um **histórico de movimentações de estoque** de cada insumo numa tabela do **Amazon DynamoDB**, um banco NoSQL: o cadastro do insumo (o saldo inicial), cada ajuste do saldo pela rota e cada débito automático do pagamento aprovado de uma OS, sempre com o saldo antes e depois. O PostgreSQL continua sendo a fonte da verdade: o saldo (`insumos.quantidade_disponivel`), a conferência do estoque na validação da OS, o débito e todas as regras não mudam. O histórico é complementar e descartável: a gravação é *best-effort*, os itens expiram em 180 dias, e nenhuma regra o lê para decidir algo. A consulta é feita em `GET /api/insumos/{id}/movimentacoes`.

> As ADRs e RFCs são numeradas em sequência única entre este monorepo e os repositórios [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658), [BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658), [EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice das decisões está no [docs/arquitetura.md](../arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

**Contexto**:
- O estoque guarda só o saldo atual de cada insumo (`insumos.quantidade_disponivel`, [ADR-012](ADR-012-Controle-de-Estoque-de-Insumos.md) e [ADR-017](ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md)). O `PUT /api/insumos` sobrescreve o saldo, e o valor anterior se perde. O débito do `PagamentoAprovado` grava o saldo novo e a marca `estoque_debitado` da OS, mas não registra quanto saiu de cada insumo. A ADR-012 aceitou ficar "sem histórico de movimentações": pela API, não há como saber de onde veio o saldo de um insumo.
- Esse registro tem o formato típico de NoSQL, como o histórico de pagamentos do Billing (ADR-024), a linha do tempo da produção do Execução e Produção (ADR-025) e o histórico da saga do OS Service (ADR-026): só recebe gravações novas (*append-only*), é lido sempre pelo insumo (uma chave), sem junções, os atributos variam conforme a movimentação (o id da OS só no débito) e pode expirar sozinho.
- A [RFC-002](../rfc/RFC-002-Escolha-do-Banco-de-Dados.md) rejeitou o DynamoDB como banco principal, porque o modelo da oficina é cheio de relacionamentos (ADR-017). Isso continua valendo: aqui, ele entra só para um dado sem relacionamentos. O saldo, que participa da validação da OS e do débito, continua no PostgreSQL.
- Um banco por serviço ([ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)): a tabela é só da Management, separada das tabelas do Billing, do Execução e Produção e do OS Service.
- É um projeto de estudo: o custo precisa ficar perto de zero, e o ambiente é destruído depois da demonstração.

**Decisão**:

1. **Papel de cada banco**:

   | Banco | O que guarda | Papel |
   |---|---|---|
   | PostgreSQL (RDS, banco `oficina`) | Os insumos, com o saldo (`insumos.quantidade_disponivel`), e a referência de cada OS, com a marca `estoque_debitado` | **Fonte da verdade**. O saldo, a conferência do estoque na validação da OS, o débito (numa transação com a marca `estoque_debitado`) e todas as regras saem dele, como antes |
   | DynamoDB (tabela `techchallenge-oficina-management-estoque-historico`) | Um item por movimentação de estoque de um insumo | **Histórico complementar e descartável**, só para consulta e auditoria. Nenhuma regra o lê; se faltar um item, nada quebra |

2. **Tabela**, em [DB/infra/dynamodb.tf](../../DB/infra/dynamodb.tf), no mesmo Terraform, state e pipeline do RDS:
   - sob demanda (`PAY_PER_REQUEST`), sem capacidade provisionada;
   - chave de partição `InsumoId` (S). As tabelas dos outros três serviços usam o id da OS; aqui, a consulta é sempre por insumo, e o débito de uma OS mexe em vários insumos;
   - chave de ordenação `OcorridoEm` (S): data e hora UTC em ISO-8601 com milissegundos, mais `#` e 8 caracteres hexadecimais (por exemplo, `2026-11-27T10:00:00.000Z#abcd1234`). A data ordena as movimentações, e o sufixo evita colisão entre dois registros no mesmo milissegundo;
   - TTL no atributo `ExpiraEm` (epoch em segundos, preenchido pela API: data da movimentação + 180 dias);
   - sem proteção contra exclusão (o Destroy apaga a tabela), sem PITR e sem streams;
   - outputs `dynamodb_historico_estoque_nome` e `dynamodb_historico_estoque_arn`.

3. **Item**, sem dados pessoais (só os ids do insumo e da OS):

   | Atributo | Conteúdo |
   |---|---|
   | `Tipo` | `Cadastro` (o saldo inicial, no `POST /api/insumos`), `Ajuste` (a mudança do saldo no `PUT /api/insumos`) ou `DebitoOrdemServico` (o débito automático do `PagamentoAprovado`) |
   | `Origem` | `acao` (rota HTTP: `Cadastro` e `Ajuste`) ou `evento` (evento consumido da fila SQS: `DebitoOrdemServico`) |
   | `Quantidade` | A variação do saldo, com sinal: positiva no `Cadastro`, positiva ou negativa no `Ajuste` e negativa no `DebitoOrdemServico` |
   | `SaldoAnterior` e `SaldoNovo` | O saldo antes e depois da movimentação. No `Cadastro`, de 0 ao saldo inicial |
   | `OrdemServicoId` | Só no `DebitoOrdemServico`: a OS cujo pagamento aprovado debitou o insumo |

   O `OcorridoEm` é o instante da movimentação, marcado pela API logo depois de gravar no PostgreSQL.

4. **Quando registra**: sempre depois de gravar no PostgreSQL, com `CancellationToken.None`, para o registro não se perder se a requisição for cancelada ou o consumidor for encerrado com o saldo já gravado:

   | Fluxo | O que registra |
   |---|---|
   | `POST /api/insumos` (`InsumoUseCases.CriarAsync`) | `Cadastro` (`acao`), de 0 ao saldo inicial |
   | `PUT /api/insumos` (`InsumoUseCases.AtualizarAsync`) | `Ajuste` (`acao`), do saldo anterior ao novo, só quando o saldo muda. Um `PUT` que muda só o nome, o fabricante ou o valor unitário não registra nada |
   | `PagamentoAprovado` (`OrdemServicoReferenciaUseCases.ProcessarPagamentoAprovadoAsync`) | Um `DebitoOrdemServico` (`evento`) por insumo debitado, com o id da OS, **depois do commit** da transação que grava o débito e a marca `estoque_debitado`: um débito desfeito (*rollback*) nunca vira item. Para isso, o `IEstoqueUseCases.DebitarEstoqueParaOrdemServicoAsync` passou a devolver a lista dos débitos (`DebitoInsumo`), com o saldo antes e depois de cada insumo |

   - Não registram nada, porque nenhum saldo mudou: o `PUT` que não muda o saldo, o `PagamentoAprovado` ignorado (OS desconhecida ou estoque já debitado) e o débito que falhou (estoque insuficiente ou serviço ou insumo inexistente: sai o `os.falha`, etapa `estoque_debito`, e nada é debitado). As requisições rejeitadas (400 e 404) também não.
   - A exclusão do insumo também não registra nada (fora do escopo): o histórico dele continua até os itens expirarem.

5. **Best-effort**:
   - `RegistrarAsync` nunca lança: uma falha (DynamoDB fora do ar, permissão negada, tabela inexistente) gera `integracao.falha` (3001) como **Warning**, com `Integracao` = `dynamodb` e `Operacao` = `registro_historico` (mais `InsumoId` e `Tipo`), e o fluxo segue: a rota responde normalmente, e o evento é processado (o consumidor apaga a mensagem). Só aquela linha falta no histórico.
   - A gravação é síncrona: com o DynamoDB fora do ar, cada cadastro, ajuste ou débito só termina depois do *timeout* e das novas tentativas do SDK.
   - `ListarAsync` registra `integracao.falha` como **Error** (`Operacao` = `consulta_historico`) e relança a falha: a rota de consulta responde 500, pelo middleware global ([ADR-013](ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md)).
   - A consulta é um `Query` na partição do insumo, em ordem crescente da chave de ordenação, com leitura consistente (`ConsistentRead`) e percorrendo todas as páginas.

6. **Rota**: `GET /api/insumos/{id}/movimentacoes`, com JWT (na AWS, `/management/api/insumos/{id}/movimentacoes`, pela rota `/management` que já existe). Responde 200 com a lista em ordem cronológica, ou com a lista vazia quando não há registros. Cada item tem `ocorridoEm`, `tipo`, `origem`, `quantidade`, `saldoAnterior`, `saldoNovo` e `ordemServicoId` (preenchido só no débito). A rota não consulta o PostgreSQL: um insumo desconhecido ou excluído também responde 200 (o excluído mantém o histórico até os itens expirarem).

7. **Código**, no padrão de porta e adapter do SQS (`IEventosPublisher`) e dos históricos dos outros três serviços (ADR-024, ADR-025 e ADR-026):
   - porta `IHistoricoEstoqueGateway`, records `MovimentacaoEstoque` (propriedades `init`) e `DebitoInsumo` e as constantes `TiposMovimentacaoEstoque` e `OrigensMovimentacaoEstoque` no projeto UseCases (`Features/Insumos/Interfaces`), com o `MovimentacaoEstoqueViewModel` e o mapeamento no `InsumoProfile`. A porta chega pelo construtor ao `InsumoUseCases` (cadastro, ajuste e consulta, `ListarMovimentacoesAsync`) e, pelo facade `IOrdemServicoReferenciaUseCasesFacade` que já existe (criado por causa da regra S107 do Sonar, sobre o número de parâmetros do construtor), ao `OrdemServicoReferenciaUseCases` (débito);
   - projeto novo `TechChallenge.Oficina.NoSql` (anel "1 - Frameworks & Drivers"), o único com o pacote `AWSSDK.DynamoDBv2` (4.0.*): `HistoricoEstoqueGateway`, a interface interna `IDynamoDbClient` com o `DynamoDbClientAdapter` (o único ponto que usa o SDK) e o `AddInfraNoSql`;
   - configuração `DynamoDbSettings`: `TabelaHistoricoEstoque` (na AWS, `DynamoDbSettings__TabelaHistoricoEstoque` no ConfigMap) e `ServiceUrl` (só localmente);
   - testes no projeto `TechChallenge.Oficina.NoSql.Tests`, com o SDK mockado; os testes de integração usam um histórico em memória.

8. **Credenciais e IAM, sem usuário nem chave novos, com a política no `SQS/infra`**: o [SQS/infra/iam.tf](../../SQS/infra/iam.tf) anexa a política inline `dynamodb-historico-estoque` (só `dynamodb:PutItem` e `dynamodb:Query`, no ARN da tabela, montado pelo nome) ao usuário IAM da Management que já existe, `techchallenge-oficina-management-sqs`. A política fica no mesmo state do usuário, e não no `DB/infra`, como nos outros três repositórios:
   - **Motivo:** neste monorepo, o Bootstrap, o Deploy e o Destroy rodam o `DB/infra` e o `SQS/infra` em paralelo. Uma política no `DB/infra` correria contra a criação do usuário (ou exigiria mudar a ordem dos jobs).
   - **Resultado:** nenhum job novo, nenhuma mudança na ordem dos jobs e nenhuma corrida. Se o usuário for recriado, a política volta no mesmo apply, sem o Bootstrap extra que os outros repositórios exigem. O IAM aceita o ARN antes de a tabela existir, então a ordem entre as duas pastas não importa.
   - O nome da tabela aparece nas duas raízes do Terraform: na tabela (`DB/infra`) e no ARN da política (`SQS/infra`).
   - A chave desse usuário já chega ao pod (SSM → Secret `oficina-api-secrets`) e passa a servir às filas e à tabela; no `_k8s-apply.yml`, o passo agora se chama "Adicionar a chave do usuário IAM da Management (SQS e DynamoDB) ao kubernetes/k8s/.env".
   - Na AWS, a API nunca cria nem descreve a tabela (sem `CreateTable` e `DescribeTable`): ela é do Terraform. As tabelas e as políticas do Billing, do Execução e Produção e do OS Service ficam nos repositórios deles, em outros usuários: nenhum serviço acessa a tabela de outro.

9. **Ambiente local**: o `docker-compose.yml` sobe o DynamoDB Local (`amazon/dynamodb-local:3.1.0`, em memória, container `oficina-dynamodb`), na porta `8003` do host (`DYNAMODB_PORT`), porque a `8000`, a `8001` e a `8002` são do Billing, do Execução e Produção e do OS Service. A API aponta para ele por `DynamoDbSettings__ServiceUrl`, com as mesmas credenciais fictícias do ElasticMQ; no `dotnet run`, o `appsettings.Development.json` usa `http://localhost:8003`. Só com `ServiceUrl` preenchido a API cria a tabela no startup (`GarantirTabelasLocaisAsync`), com as mesmas chaves e o mesmo TTL; uma falha aí só gera um aviso, e a API sobe.

**Consequências**:
- ✅ Dá para ver, por uma rota, de onde veio o saldo de cada insumo (o saldo inicial, cada ajuste e cada débito, com o saldo antes e depois e a OS do débito), sem mudar a tabela `insumos` nem as regras do estoque (ADR-012).
- ✅ O débito de uma OS fica ligado ao insumo pelo `OrdemServicoId`, o mesmo dos logs `evento.*` e do histórico da saga do OS Service (ADR-026).
- ✅ Custo perto de zero: tabela sob demanda, poucos itens e expiração automática, sem instância ligada.
- ✅ Uma falha no DynamoDB não derruba os cadastros nem o débito: as rotas respondem, e o consumo dos eventos segue.
- ✅ Nenhum job novo nas pipelines: a tabela entra no `DB/infra` e a política no `SQS/infra`, que já rodam no Bootstrap, no Deploy e no Destroy (só o nome de um passo do `_k8s-apply.yml` mudou).
- ✅ O mesmo padrão dos outros três serviços (ADR-024, ADR-025 e ADR-026): agora os quatro usam NoSQL onde o formato combina (gravação e leitura por chave) e relacional onde há relacionamentos, como recomenda a RFC-002.
- ⚠ **O histórico pode ter buracos**: uma falha de gravação perde aquela linha (fica só o log `integracao.falha`), e os itens somem depois de 180 dias. O saldo que vale continua sendo o do PostgreSQL.
- ⚠ **Sem atomicidade entre os dois bancos**: as gravações são separadas. Se o processo cair entre elas, o histórico fica sem o registro. No débito, a nova entrega do `PagamentoAprovado` é ignorada (o estoque já foi debitado), então o item não volta.
- ⚠ **O histórico mostra o que cada operação viu**: o `PUT` do insumo e o débito do `PagamentoAprovado` não têm controle de concorrência (já era assim antes desta ADR). Se os dois mexerem no mesmo insumo ao mesmo tempo, o último a gravar vence no PostgreSQL, e os saldos de dois itens seguidos podem não se encadear.
- ⚠ **Mais latência com o DynamoDB fora do ar**: como a gravação é síncrona, o cadastro, o ajuste e o débito esperam o *timeout* e as novas tentativas do SDK antes de seguir.
- ⚠ **O insumo excluído mantém o histórico** até os itens expirarem: a exclusão não apaga nada no DynamoDB (os itens não têm dados pessoais).
- ⚠ **O monitor de erros de integração também alerta para `dynamodb`**: a métrica `oficina.integracao.erros` (filtro `@Integracao:*`) conta inclusive o Warning do registro ([observabilidade.md](../observabilidade.md)).
- ⚠ **O nome da tabela fica em dois lugares**: no `DB/infra` (a tabela) e no `SQS/infra` (o ARN da política). Renomear a tabela exige mudar os dois. O usuário `terraform` da CI precisa de permissões no DynamoDB e de `iam:PutUserPolicy`, `iam:GetUserPolicy` e `iam:DeleteUserPolicy` (hoje ele é administrador).
- ⚠ **Mais uma peça local**: o DynamoDB Local roda em memória, e o histórico some quando o container reinicia.

**Alternativas Rejeitadas**:
- Registrar só os débitos de estoque de cada OS:
  - rejeitada porque cobre menos (o cadastro e os ajustes, que sobrescrevem o saldo, ficariam sem registro) e repetiria o recorte por OS do histórico da saga do OS Service (ADR-026). O histórico por insumo explica o saldo inteiro.
- Histórico das notificações de mudança de status (o e-mail ao cliente):
  - rejeitada porque o e-mail vem desligado por padrão ([ADR-011](ADR-011-Envio-de-Email-de-Orcamento.md)), e a maioria dos itens diria só "desligado". Também exigiria mudar a porta do e-mail, para ela devolver o resultado do envio.
- A política no `DB/infra`, como nos outros três repositórios:
  - rejeitada porque exigiria mudar a ordem dos jobs no Bootstrap, no Deploy e no Destroy, para a política não correr contra a criação do usuário. E, quando o `SQS/infra` recriasse o usuário sem mudança no `DB/infra`, a política sumiria até um novo apply do `DB/infra`.
- Levar o estoque (os insumos) para o DynamoDB e tirar o saldo do RDS:
  - rejeitada porque o saldo participa da conferência na validação da OS e do débito, numa transação com a marca `estoque_debitado` da referência da OS (ADR-012), e os insumos se ligam aos serviços por chave estrangeira (`servicos_insumos`, ADR-017). O DynamoDB entraria no caminho crítico dos cadastros e do consumidor SQS. O histórico mostra o uso de NoSQL sem esse risco.
- Uma tabela de movimentações no PostgreSQL:
  - rejeitada porque mudaria o modelo relacional (mais uma tabela e uma migration) e a ADR-012 por um registro que não é regra de negócio, e não teria expiração nativa (exigiria uma rotina de limpeza).
- Um usuário IAM e uma chave só para o DynamoDB:
  - rejeitada porque traria novos parâmetros no SSM, novas chaves no Secret e credenciais explícitas no código, sem ganho real: a política inline já limita o acesso a `PutItem` e `Query` nesta tabela.

**Fora do escopo**: a exclusão do insumo e o `PUT` que não muda o saldo; os débitos que falharam ou foram ignorados (a falha já sai no `os.falha`, etapa `estoque_debito`, e o pagamento aprovado da OS aparece no histórico da saga do OS Service); quem fez o ajuste (o JWT traz o CPF, um dado pessoal); corrigir a concorrência entre o `PUT` e o débito, que já existia (não há token de concorrência; o histórico mostra o que cada operação viu); levar o estoque para o NoSQL; PITR (backup contínuo); DynamoDB Streams; métricas e monitores específicos do DynamoDB no Datadog (o `integracao.falha` já alimenta a `oficina.integracao.erros`).

**Relacionados**: [ADR-004](ADR-004-Gateways-e-Persistencia.md), [ADR-011](ADR-011-Envio-de-Email-de-Orcamento.md), [ADR-012](ADR-012-Controle-de-Estoque-de-Insumos.md), [ADR-013](ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md), [ADR-014](ADR-014-Observabilidade-com-Datadog.md), [ADR-017](ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md), [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e [RFC-002](../rfc/RFC-002-Escolha-do-Banco-de-Dados.md) (este repo), [ADR-024](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-024-Historico-de-Pagamentos-no-DynamoDB.md) (repo BILLING), [ADR-025](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-025-Linha-do-Tempo-da-Producao-no-DynamoDB.md) (repo EXECUTION) e [ADR-026](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-026-Historico-da-Saga-no-DynamoDB.md) (repo OS-SERVICE).
