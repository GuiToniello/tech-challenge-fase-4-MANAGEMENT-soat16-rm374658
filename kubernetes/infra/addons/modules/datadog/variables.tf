variable "env" {
  type = string
}

variable "site" {
  type = string
}

# Handles no fim das mensagens dos alertas (ex.: "@nome@dominio.com"). Vazio: alertas so na UI.
variable "alert_recipients" {
  type      = string
  default   = ""
  sensitive = true
}

variable "synthetics_status" {
  type    = string
  default = "live"
}

# 300 s = 5 min: 1 teste por API (hoje 2: management e os), ~288 execucoes/dia por teste, coberto pelo trial.
variable "synthetics_tick_every" {
  type    = number
  default = 300
}

variable "synthetics_locations" {
  type    = list(string)
  default = ["aws:us-east-1"]
}

# HTTP API criada pela foundation. O endpoint muda a cada bootstrap, entao e lido pelo nome.
variable "api_gateway_name" {
  type    = string
  default = "techchallenge-oficina-api"
}