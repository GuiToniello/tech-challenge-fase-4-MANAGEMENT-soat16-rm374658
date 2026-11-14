locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }

  availability_zones = slice(data.aws_availability_zones.available.names, 0, 2)

  # NodePort de cada API (tambem porta do listener e do target group no NLB). A chave vira o
  # prefixo da rota no API Gateway (/management/...) e o sufixo do target group.
  # Precisa ser igual ao nodePort de kubernetes/k8s/features/<api>-api/service.yml.
  api_node_ports = {
    management = 30080
  }

  # Lambdas do repo LAMBDA (AWS SAM), referenciadas pelo nome fixo: o apply nao depende delas
  # ja existirem. A permissao para o API Gateway invoca-las fica nos templates SAM.
  lambda_arn_prefix      = "arn:aws:lambda:${var.aws_region}:${data.aws_caller_identity.current.account_id}:function:${var.project_name}"
  lambda_authorizer_arn  = "${local.lambda_arn_prefix}-authorizer"
  lambda_issue_token_arn = "${local.lambda_arn_prefix}-issue-token"
}