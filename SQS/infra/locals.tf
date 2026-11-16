locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }

  # Uma fila FIFO (com DLQ) e um usuario IAM por servico: management (este repo) e os-service
  # (repo OS-SERVICE). Cada servico consome a propria fila e publica na fila do outro (publishes_to).
  # A chave tambem entra no caminho dos parametros SSM (/<project>/sqs/<servico>/...).
  services = {
    "management" = {
      queue_name   = "${var.project_name}-management.fifo"
      dlq_name     = "${var.project_name}-management-dlq.fifo"
      user_name    = "${var.project_name}-management-sqs"
      publishes_to = "os-service"
    }
    "os-service" = {
      queue_name   = "${var.project_name}-os-service.fifo"
      dlq_name     = "${var.project_name}-os-service-dlq.fifo"
      user_name    = "${var.project_name}-os-service-sqs"
      publishes_to = "management"
    }
  }
}
