# Um usuario IAM por servico. A chave de acesso (ssm.tf) chega aos pods pelo Secret do Kubernetes.
# force_destroy: o destroy remove o usuario mesmo com chaves criadas fora do Terraform.
resource "aws_iam_user" "sqs" {
  for_each = local.services

  name          = each.value.user_name
  force_destroy = true
}

# Cada servico consome so a propria fila e publica so na fila do outro servico.
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
    sid = "PublicarNaFilaDoOutroServico"
    actions = [
      "sqs:SendMessage",
      "sqs:GetQueueUrl",
    ]
    resources = [aws_sqs_queue.this[each.value.publishes_to].arn]
  }
}

resource "aws_iam_user_policy" "sqs" {
  for_each = local.services

  name   = "sqs"
  user   = aws_iam_user.sqs[each.key].name
  policy = data.aws_iam_policy_document.sqs[each.key].json
}
