locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }

  # Uma fila FIFO (com DLQ) e um usuario IAM por servico: management (este repo), os-service
  # (repo OS-SERVICE) e billing (repo BILLING). Cada servico consome a propria fila e publica nas
  # filas dos outros dois (publishes_to): o fan-out e feito por quem publica, um envio por fila.
  # A chave tambem entra no caminho dos parametros SSM (/<project>/sqs/<servico>/...).
  services = {
    "management" = {
      queue_name   = "${var.project_name}-management.fifo"
      dlq_name     = "${var.project_name}-management-dlq.fifo"
      user_name    = "${var.project_name}-management-sqs"
      publishes_to = ["os-service", "billing"]
    }
    "os-service" = {
      queue_name   = "${var.project_name}-os-service.fifo"
      dlq_name     = "${var.project_name}-os-service-dlq.fifo"
      user_name    = "${var.project_name}-os-service-sqs"
      publishes_to = ["management", "billing"]
    }
    "billing" = {
      queue_name   = "${var.project_name}-billing.fifo"
      dlq_name     = "${var.project_name}-billing-dlq.fifo"
      user_name    = "${var.project_name}-billing-sqs"
      publishes_to = ["os-service", "management"]
    }
  }
}
