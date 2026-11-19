locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }

  # Uma fila FIFO (com DLQ) e um usuario IAM por servico: management (este repo), os-service
  # (repo OS-SERVICE), billing (repo BILLING) e execution (repo EXECUTION, Execucao e Producao).
  # Cada servico consome a propria fila e publica nas filas dos servicos que consomem algum evento
  # dele (publishes_to): o fan-out e feito por quem publica, um envio por fila. O execution so
  # publica para o os-service, e o billing nao publica para o execution.
  # A chave tambem entra no caminho dos parametros SSM (/<project>/sqs/<servico>/...).
  services = {
    "management" = {
      queue_name   = "${var.project_name}-management.fifo"
      dlq_name     = "${var.project_name}-management-dlq.fifo"
      user_name    = "${var.project_name}-management-sqs"
      publishes_to = ["os-service", "billing", "execution"]
    }
    "os-service" = {
      queue_name   = "${var.project_name}-os-service.fifo"
      dlq_name     = "${var.project_name}-os-service-dlq.fifo"
      user_name    = "${var.project_name}-os-service-sqs"
      publishes_to = ["management", "billing", "execution"]
    }
    "billing" = {
      queue_name   = "${var.project_name}-billing.fifo"
      dlq_name     = "${var.project_name}-billing-dlq.fifo"
      user_name    = "${var.project_name}-billing-sqs"
      publishes_to = ["os-service", "management"]
    }
    "execution" = {
      queue_name   = "${var.project_name}-execution.fifo"
      dlq_name     = "${var.project_name}-execution-dlq.fifo"
      user_name    = "${var.project_name}-execution-sqs"
      publishes_to = ["os-service"]
    }
  }
}
