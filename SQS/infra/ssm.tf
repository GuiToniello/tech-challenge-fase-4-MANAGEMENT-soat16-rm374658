# Chave de acesso de cada usuario no SSM Parameter Store (SecureString, tier Standard, sem custo).
# O _k8s-apply.yml de cada repo (MANAGEMENT, OS-SERVICE e BILLING) le os parametros do proprio servico e
# grava AWS_ACCESS_KEY_ID e AWS_SECRET_ACCESS_KEY no Secret dos pods. O segredo tambem fica no
# state (bucket S3 criptografado), como a senha do RDS.
resource "aws_iam_access_key" "sqs" {
  for_each = local.services

  user = aws_iam_user.sqs[each.key].name
}

resource "aws_ssm_parameter" "access_key_id" {
  for_each = local.services

  name  = "/${var.project_name}/sqs/${each.key}/access-key-id"
  type  = "SecureString"
  tier  = "Standard"
  value = aws_iam_access_key.sqs[each.key].id
}

resource "aws_ssm_parameter" "secret_access_key" {
  for_each = local.services

  name  = "/${var.project_name}/sqs/${each.key}/secret-access-key"
  type  = "SecureString"
  tier  = "Standard"
  value = aws_iam_access_key.sqs[each.key].secret
}
