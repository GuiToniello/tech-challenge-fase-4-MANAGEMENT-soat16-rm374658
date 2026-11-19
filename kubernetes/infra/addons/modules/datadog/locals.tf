locals {
  # Prefixo da rota no API Gateway -> service no Datadog (nome do Deployment em kubernetes/k8s/features
  # deste repo e dos repos OS-SERVICE, BILLING e EXECUTION).
  services = {
    management = "management-api"
    os         = "os-service-api"
    billing    = "billing-api"
    execution  = "execution-api"
  }

  common_tags = [
    "env:${var.env}",
    "project:techchallenge-oficina",
    "managed-by:terraform",
  ]

  # Escopo das metricas de Kubernetes das APIs.
  namespace_scope = "kube_namespace:oficina"

  # Host da UI: US1 e EU usam app.<site>; nos demais sites o host e o proprio site (ex.: us5.datadoghq.com).
  app_host = contains(["datadoghq.com", "datadoghq.eu"], var.site) ? "app.${var.site}" : var.site
}