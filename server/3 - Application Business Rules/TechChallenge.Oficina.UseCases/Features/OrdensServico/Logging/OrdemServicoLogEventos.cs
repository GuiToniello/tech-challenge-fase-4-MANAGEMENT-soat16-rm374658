using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;

// Catálogo dos eventos de log de negócio da ordem de serviço. Nomes e valores são o contrato com as métricas e monitores do Datadog (docs/observabilidade.md e Terraform em kubernetes/infra/addons): não renomear sem atualizar os dois lados.
// O ciclo de vida da OS (os.criada, os.status_alterado, os.encerrada) pertence ao OS Service; o Management registra o orçamento e a troca de eventos via SQS.
public static class OrdemServicoLogEventos
{
    public static readonly EventId OsOrcamentoGerado = new(1004, "os.orcamento_gerado");
    public static readonly EventId OsOrcamentoEnviado = new(1005, "os.orcamento_enviado");
    public static readonly EventId OsFalha = new(2001, "os.falha");
    public static readonly EventId IntegracaoFalha = new(3001, "integracao.falha");
    public static readonly EventId EventoPublicado = new(4001, "evento.publicado");
    public static readonly EventId EventoConsumido = new(4002, "evento.consumido");
    public static readonly EventId EventoIgnorado = new(4003, "evento.ignorado");

    public static class Etapas
    {
        public const string Abertura = "abertura";
        public const string Atualizacao = "atualizacao";
        public const string OrcamentoGeracao = "orcamento_geracao";
        public const string OrcamentoEnvio = "orcamento_envio";
        public const string OrcamentoAprovacao = "orcamento_aprovacao";
        public const string OrcamentoRecusa = "orcamento_recusa";
    }

    public static class TiposFalha
    {
        public const string RegraNegocio = "regra_negocio";
        public const string NaoEncontrado = "nao_encontrado";
    }

    public static class Integracoes
    {
        public const string Resend = "resend";
        public const string Postgres = "postgres";
        public const string Sqs = "sqs";
    }

    public static class Operacoes
    {
        public const string EnvioOrcamento = "envio_orcamento";
        public const string NotificacaoStatus = "notificacao_status";
        public const string Publicacao = "publicacao";
        public const string Consumo = "consumo";
    }

    public static string StatusParaLog(StatusOrdemServico status)
    {
        return status switch
        {
            StatusOrdemServico.Recebida => "recebida",
            StatusOrdemServico.EmDiagnostico => "em_diagnostico",
            StatusOrdemServico.AguardandoAprovacao => "aguardando_aprovacao",
            StatusOrdemServico.EmExecucao => "em_execucao",
            StatusOrdemServico.Finalizada => "finalizada",
            StatusOrdemServico.Entregue => "entregue",
            StatusOrdemServico.Encerrada => "encerrada",
            StatusOrdemServico.Cancelada => "cancelada",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status da ordem de servico invalido.")
        };
    }
}
