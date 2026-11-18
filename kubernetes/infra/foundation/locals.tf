locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }

  availability_zones = slice(data.aws_availability_zones.available.names, 0, 2)

  # NodePort de cada API (tambem porta do listener e do target group no NLB). A chave vira o
  # prefixo da rota no API Gateway (/management/..., /os/..., /billing/...) e o sufixo do target group.
  # Precisa ser igual ao nodePort do service.yml da API: kubernetes/k8s/features/management-api/
  # (este repo), kubernetes/k8s/features/os-service-api/ (repo OS-SERVICE) e
  # kubernetes/k8s/features/billing-api/ (repo BILLING).
  api_node_ports = {
    management = 30080
    os         = 30081
    billing    = 30082
  }

  # Lambdas do repo LAMBDA (AWS SAM), referenciadas pelo nome fixo: o apply nao depende delas
  # ja existirem. A permissao para o API Gateway invoca-las fica nos templates SAM.
  lambda_arn_prefix      = "arn:aws:lambda:${var.aws_region}:${data.aws_caller_identity.current.account_id}:function:${var.project_name}"
  lambda_authorizer_arn  = "${local.lambda_arn_prefix}-authorizer"
  lambda_issue_token_arn = "${local.lambda_arn_prefix}-issue-token"
}