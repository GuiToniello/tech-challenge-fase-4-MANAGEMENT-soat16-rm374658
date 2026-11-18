output "cluster_name" {
  value = aws_eks_cluster.this.name
}

output "cluster_endpoint" {
  value = aws_eks_cluster.this.endpoint
}

output "cluster_region" {
  value = var.aws_region
}

output "api_gateway_endpoint" {
  value = aws_apigatewayv2_api.this.api_endpoint
}

# URL de notificacao do Mercado Pago. O _k8s-apply.yml do repo BILLING monta a mesma URL a partir
# do endpoint do API Gateway (descoberto pelo nome da API) e a grava no Secret do Billing.
output "billing_webhook_url" {
  value = "${aws_apigatewayv2_api.this.api_endpoint}/billing/api/pagamentos/notificacoes"
}