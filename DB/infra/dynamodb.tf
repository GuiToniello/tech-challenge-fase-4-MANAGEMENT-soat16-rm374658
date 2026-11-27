# Historico de movimentacoes de estoque da Management em NoSQL (DynamoDB), complementar ao RDS: um
# item por movimentacao de estoque de cada insumo (Cadastro, Ajuste e DebitoOrdemServico).
# O PostgreSQL (rds.tf) continua sendo a fonte da verdade do saldo (insumos.quantidade_disponivel): a
# API grava aqui em best-effort, e uma falha no DynamoDB nao quebra o cadastro, o ajuste nem o debito
# do estoque. Os itens sao descartaveis e expiram pelo TTL (ExpiraEm, epoch em segundos preenchido
# pela API, 180 dias apos a movimentacao).
# Chave: InsumoId (particao) + OcorridoEm (ordenacao, data/hora em texto), para consultar o historico
# de um insumo em ordem cronologica.
# Sob demanda, sem PITR e sem streams; deletion_protection desligado para o Destroy funcionar.
# As tags vem do default_tags do provider.
# A politica IAM desta tabela fica em SQS/infra/iam.tf, e nao aqui: ela vai para o usuario IAM da
# Management (techchallenge-oficina-management-sqs), que e criado no state do SQS/infra. O DB/infra e o
# SQS/infra rodam em paralelo nas pipelines, entao, no mesmo state do usuario, a politica nunca e
# aplicada antes de ele existir e, se ele for recriado, volta no mesmo apply.
resource "aws_dynamodb_table" "historico_estoque" {
  name                        = "${var.project_name}-management-estoque-historico"
  billing_mode                = "PAY_PER_REQUEST"
  hash_key                    = "InsumoId"
  range_key                   = "OcorridoEm"
  deletion_protection_enabled = false

  attribute {
    name = "InsumoId"
    type = "S"
  }

  attribute {
    name = "OcorridoEm"
    type = "S"
  }

  ttl {
    attribute_name = "ExpiraEm"
    enabled        = true
  }
}
