# Sem segredos: as chaves de acesso ficam so no SSM (e no state).
output "queue_urls" {
  value = { for k, q in aws_sqs_queue.this : k => q.url }
}

output "queue_arns" {
  value = { for k, q in aws_sqs_queue.this : k => q.arn }
}

output "dlq_urls" {
  value = { for k, q in aws_sqs_queue.dlq : k => q.url }
}

output "iam_user_names" {
  value = { for k, u in aws_iam_user.sqs : k => u.name }
}

output "ssm_parameter_names" {
  value = {
    for k in keys(local.services) : k => {
      access_key_id     = aws_ssm_parameter.access_key_id[k].name
      secret_access_key = aws_ssm_parameter.secret_access_key[k].name
    }
  }
}
