# Filas FIFO entre a Management API, o OS Service e o Billing. Uma mensagem recebida 5 vezes sem ser
# apagada (maxReceiveCount) vai para a DLQ do servico, que a guarda por 14 dias para analise.

resource "aws_sqs_queue" "dlq" {
  for_each = local.services

  name                      = each.value.dlq_name
  fifo_queue                = true
  message_retention_seconds = 1209600 # 14 dias, o maximo do SQS
}

# Sem deduplicacao por conteudo: quem publica informa o MessageDeduplicationId e o MessageGroupId.
resource "aws_sqs_queue" "this" {
  for_each = local.services

  name                        = each.value.queue_name
  fifo_queue                  = true
  content_based_deduplication = false
  visibility_timeout_seconds  = 60

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.dlq[each.key].arn
    maxReceiveCount     = 5
  })
}

# So a fila principal do proprio servico pode usar a DLQ. Fica em recurso separado porque a fila
# principal ja referencia a DLQ (redrive_policy): no proprio aws_sqs_queue.dlq criaria um ciclo.
resource "aws_sqs_queue_redrive_allow_policy" "dlq" {
  for_each = local.services

  queue_url = aws_sqs_queue.dlq[each.key].id

  redrive_allow_policy = jsonencode({
    redrivePermission = "byQueue"
    sourceQueueArns   = [aws_sqs_queue.this[each.key].arn]
  })
}
