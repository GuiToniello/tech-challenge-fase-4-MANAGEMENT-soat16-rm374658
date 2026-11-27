# Um usuario IAM por servico. A chave de acesso (ssm.tf) chega aos pods pelo Secret do Kubernetes.
# force_destroy: o destroy remove o usuario mesmo com chaves criadas fora do Terraform.
resource "aws_iam_user" "sqs" {
  for_each = local.services

  name          = each.value.user_name
  force_destroy = true
}

# Cada servico consome so a propria fila e publica so nas filas listadas em publishes_to (servicos
# que consomem algum evento dele).
data "aws_iam_policy_document" "sqs" {
  for_each = local.services

  statement {
    sid = "ConsumirFilaPropria"
    actions = [
      "sqs:ReceiveMessage",
      "sqs:DeleteMessage",
      "sqs:ChangeMessageVisibility",
      "sqs:GetQueueAttributes",
      "sqs:GetQueueUrl",
    ]
    resources = [aws_sqs_queue.this[each.key].arn]
  }

  statement {
    sid = "PublicarNasFilasDosOutrosServicos"
    actions = [
      "sqs:SendMessage",
      "sqs:GetQueueUrl",
    ]
    resources = [for destino in each.value.publishes_to : aws_sqs_queue.this[destino].arn]
  }
}

resource "aws_iam_user_policy" "sqs" {
  for_each = local.services

  name   = "sqs"
  user   = aws_iam_user.sqs[each.key].name
  policy = data.aws_iam_policy_document.sqs[each.key].json
}

# Permissao da API da Management na tabela DynamoDB do historico de movimentacoes de estoque, criada
# pelo DB/infra deste repo (DB/infra/dynamodb.tf). Fica aqui, e nao no DB/infra, para ficar no mesmo
# state do usuario IAM da Management: o DB/infra e o SQS/infra rodam em paralelo no Bootstrap, no Deploy
# e no Destroy, entao a politica nunca e aplicada antes de o usuario existir e, se ele for recriado,
# volta no mesmo apply. O pod reaproveita a mesma chave de acesso (SSM -> Secret), sem chave ou secret
# novos. Politica inline com nome proprio, separada da politica "sqs", e so para esta tabela.
# O ARN e montado pelo nome (locals.tf), porque a tabela fica em outro state; o IAM aceita o ARN antes
# de a tabela existir.
# As tabelas e as politicas do OS Service, do Billing e do Execucao e Producao ficam nos repos de cada
# servico (OS-SERVICE, BILLING e EXECUTION), anexadas aos usuarios criados acima.
data "aws_caller_identity" "current" {}

data "aws_iam_policy_document" "dynamodb_management" {
  statement {
    sid = "HistoricoEstoque"
    actions = [
      "dynamodb:PutItem",
      "dynamodb:Query",
    ]
    resources = ["arn:aws:dynamodb:${var.aws_region}:${data.aws_caller_identity.current.account_id}:table/${local.tabela_historico_estoque_management}"]
  }
}

resource "aws_iam_user_policy" "dynamodb_management" {
  name   = "dynamodb-historico-estoque"
  user   = aws_iam_user.sqs["management"].name
  policy = data.aws_iam_policy_document.dynamodb_management.json
}
