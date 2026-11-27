# RFC-002: Escolha do banco de dados

> Uma RFC (Request for Comments) é uma proposta escrita: apresenta um problema, compara as opções e recomenda uma, para que a decisão seja discutida antes de ser adotada. As RFCs e ADRs são numeradas em sequência única entre este monorepo e os repositórios [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658), [BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658), [EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice está no [docs/arquitetura.md](../arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

- **Status**: Aceita
- **Partes afetadas**: neste monorepo, `DB/` (instância do banco) e `server/` (tabelas e acesso aos dados); fora dele, o repo LAMBDA (consulta de clientes)
- **Resultado**: PostgreSQL no Amazon RDS. A decisão está registrada na [ADR-017](../adr/ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md), junto com o diagrama ER

## 1. Resumo

Os dados da oficina (clientes, veículos, serviços, peças, ordens de serviço e orçamentos) são muito ligados entre si e precisam de regras que impeçam dados inconsistentes. A proposta é usar um banco **relacional**, o **PostgreSQL**, como serviço gerenciado no **Amazon RDS**.

## 2. Contexto

- A Management API lê e grava os dados ([ADR-015](../adr/ADR-015-Comunicacao-Sincrona-via-API-Gateway.md)), e a Lambda de autenticação (repo LAMBDA) consulta a tabela de clientes.
- O código já usa o Entity Framework Core, que cria as tabelas por migrations quando a API sobe ([ADR-007](../adr/ADR-007-EFCore-com-PostgreSQL.md) e [ADR-009](../adr/ADR-009-Migracoes-Automaticas-no-Startup.md)).
- Localmente, o `docker-compose.yml` da raiz roda o PostgreSQL 17.
- A nuvem é a AWS ([RFC-001](RFC-001-Escolha-da-Nuvem.md)), e o banco fica nas sub-redes privadas criadas pela foundation (`kubernetes/infra/foundation`).
- É um projeto de estudo: o custo precisa ser baixo, e o ambiente é destruído depois da demonstração.

## 3. Requisitos

| # | Requisito | Exemplo no sistema |
|---|---|---|
| B1 | Relacionamentos garantidos pelo banco (chaves estrangeiras) | Uma OS aponta para um cliente e um veículo que precisam existir |
| B2 | Valores que não podem se repetir | CPF/CNPJ do cliente e placa do veículo, mesmo com várias requisições gravando ao mesmo tempo |
| B3 | Transações: gravar tudo ou nada | A OS, os serviços dela e o histórico de status são gravados juntos |
| B4 | Consultas com filtros e junções | Listar as OS por status e antiguidade ou por cliente, e calcular os indicadores |
| B5 | Bom suporte no .NET | Reaproveitar o Entity Framework Core e as migrations que já existem |
| B6 | Serviço gerenciado, em rede privada, criado por Terraform | Não administrar servidor; acesso só de dentro da rede do projeto |
| B7 | Custo baixo | Projeto de estudo |

## 4. Opções avaliadas

| Opção | Tipo | Prós | Contras |
|---|---|---|---|
| **PostgreSQL no Amazon RDS** | Relacional, gerenciado | Atende B1 a B4. Tem tipos nativos que o modelo usa (identificador `uuid`, intervalo de tempo, data com fuso e decimal). Provider maduro no Entity Framework Core (Npgsql). É o mesmo banco do ambiente local. Código aberto, e uma instância pequena basta | Cobrado por hora enquanto existe. Para crescer, troca-se por uma instância maior |
| Amazon Aurora PostgreSQL | Relacional, gerenciado e compatível com PostgreSQL | Alta disponibilidade e réplicas de leitura | Pensado para cargas maiores: as instâncias fixas começam em tamanhos maiores que a `db.t3.micro`, e o modo serverless traz regras de escala e de cobrança que o projeto não precisa |
| MySQL no Amazon RDS | Relacional, gerenciado | Também atende B1 a B4, com custo parecido | Não tem tipos nativos de `uuid` nem de intervalo de tempo. Exigiria refazer as migrations e o ambiente local |
| SQL Server no Amazon RDS | Relacional, gerenciado | Muito usado com .NET | As edições completas têm licença paga, e a Express, gratuita, é limitada. Exigiria refazer as migrations |
| Amazon DynamoDB | NoSQL (chave-valor), serverless | Paga por uso e escala sozinho | Sem junções nem chaves estrangeiras: a integridade iria para o código, e o modelo teria de ser redesenhado. Não tem provider oficial no Entity Framework Core |
| MongoDB (Atlas ou Amazon DocumentDB) | NoSQL (documentos) | Estrutura flexível | Documentos não têm chaves estrangeiras: dados tão relacionados ficariam repetidos ou com a integridade só no código |
| PostgreSQL num pod do próprio EKS | Relacional, operado pelo time | Sem custo de uma instância separada | Backup, atualização, disco e recuperação ficariam por conta do time. Ocuparia o cluster pequeno, de 2 nós `t3.small` ([ADR-016](../adr/ADR-016-Escalabilidade-com-HPA.md)) |

**Comparação:** só as opções relacionais atendem bem a B1 a B4. Entre elas, o PostgreSQL no RDS é a única que, ao mesmo tempo, reaproveita tudo o que já existe (B5), é gerenciada (B6) e tem o menor custo (B7).

## 5. Proposta

Usar o **PostgreSQL no Amazon RDS**, com a configuração mais simples que atende ao projeto (detalhes na [seção 1 do DB/README.md](../../DB/README.md#1-o-que-é-provisionado)):
- instância `db.t3.micro`, com 20 GiB de disco `gp3` criptografado;
- uma zona de disponibilidade (single-AZ), sem backup automático e sem proteção contra exclusão, porque o ambiente é descartável;
- sem acesso pela internet: o banco fica nas sub-redes privadas, e só os nós do EKS e a Lambda de consulta de clientes conectam na porta 5432;
- o banco `oficina` nasce vazio, e as tabelas são criadas pelas migrations da Management API.

## 6. Riscos e como reduzi-los

| Risco | Como reduzir |
|---|---|
| Uma zona só e sem backup: um Destroy ou uma falha apagam os dados | Aceito num ambiente de estudo. Em produção: Multi-AZ, backup automático e proteção contra exclusão |
| Banco compartilhado com a Lambda buscar-cliente (repo LAMBDA), que lê a tabela `clientes`: uma mudança nessa tabela também afeta a Lambda | Migrations versionadas em `server/`, aplicadas quando a API sobe; ao mudar `clientes`, revisar a consulta da Lambda |
| A versão do PostgreSQL não está fixada no Terraform: o RDS usa a versão padrão da AWS ao criar o banco, e o ambiente local usa a 17 | Fixar `engine_version` no [rds.tf](../../DB/infra/rds.tf) numa evolução |
| A carga crescer além da instância | Trocar a classe da instância ou criar réplicas de leitura |

## 7. Resultado

Aceita e implementada na pasta [DB/](../../DB/README.md) deste monorepo. A justificativa formal, o diagrama ER e a explicação dos relacionamentos estão na [ADR-017](../adr/ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md).

**Atualização (fase 4):** o Amazon DynamoDB, rejeitado aqui como banco principal, passou a ser usado no Billing, no OS Service e no Execução e Produção, cada um com a própria tabela, para o histórico dos eventos de pagamento, o histórico da saga e a linha do tempo da produção: dados que só recebem gravações novas, lidos pela OS, sem relacionamentos e que podem expirar. O PostgreSQL continua sendo o banco de cada serviço e a fonte da verdade ([ADR-024](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-024-Historico-de-Pagamentos-no-DynamoDB.md), repo BILLING, [ADR-025](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-025-Linha-do-Tempo-da-Producao-no-DynamoDB.md), repo EXECUTION, e [ADR-026](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-026-Historico-da-Saga-no-DynamoDB.md), repo OS-SERVICE).
