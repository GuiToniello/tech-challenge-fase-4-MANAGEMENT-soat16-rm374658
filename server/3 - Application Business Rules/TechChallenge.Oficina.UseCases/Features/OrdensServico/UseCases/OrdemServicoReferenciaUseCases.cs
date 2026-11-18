using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Clientes;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.UseCases.Features.Clientes.UseCases;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Commands;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.UseCases.Features.Servicos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Veiculos.UseCases;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

// Mantem a referencia local da ordem de servico a partir dos eventos do OS Service e debita o estoque quando o Billing aprova o pagamento (coreografia via SQS).
public sealed class OrdemServicoReferenciaUseCases : IOrdemServicoReferenciaUseCases
{
    private static readonly StatusOrdemServico[] StatusNotificados =
    [
        StatusOrdemServico.EmDiagnostico,
        StatusOrdemServico.EmExecucao,
        StatusOrdemServico.Finalizada,
        StatusOrdemServico.Entregue
    ];

    private readonly IOrdemServicoReferenciaGateway _referenciaGateway;
    private readonly IClienteGateway _clienteGateway;
    private readonly IVeiculoGateway _veiculoGateway;
    private readonly IServicoGateway _servicoGateway;
    private readonly IOrdemServicoReferenciaUseCasesFacade _facade;
    private readonly ILogger<OrdemServicoReferenciaUseCases> _logger;

    public OrdemServicoReferenciaUseCases(
        IOrdemServicoReferenciaGateway referenciaGateway,
        IClienteGateway clienteGateway,
        IVeiculoGateway veiculoGateway,
        IServicoGateway servicoGateway,
        IOrdemServicoReferenciaUseCasesFacade facade,
        ILogger<OrdemServicoReferenciaUseCases> logger)
    {
        _referenciaGateway = referenciaGateway;
        _clienteGateway = clienteGateway;
        _veiculoGateway = veiculoGateway;
        _servicoGateway = servicoGateway;
        _facade = facade;
        _logger = logger;
    }

    public async Task<ResultadoProcessamentoEvento> RegistrarAsync(RegistrarOrdemServicoReferenciaCommand command, CancellationToken cancellationToken = default)
    {
        if (command.OrdemServicoId == Guid.Empty)
        {
            // Sem o identificador nao ha como correlacionar a rejeicao com a OS: a excecao devolve a mensagem para a fila (nova tentativa e, depois, DLQ)
            throw new DomainException("O identificador da ordem de servico e obrigatorio.");
        }

        var servicoIds = command.ServicoIds ?? [];
        var existente = await _referenciaGateway.ObterPorIdAsync(command.OrdemServicoId, cancellationToken);
        Cliente cliente;
        IReadOnlyCollection<Servico> servicos;

        try
        {
            (cliente, servicos) = await ValidarDadosAsync(command.ClienteId, command.VeiculoId, servicoIds, cancellationToken);
        }
        catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
        {
            await RejeitarAsync(command.OrdemServicoId, existente, exception, cancellationToken);
            return ResultadoProcessamentoEvento.Processado;
        }

        if (existente is null)
        {
            var referencia = OrdemServicoReferencia.Criar(command.OrdemServicoId, command.ClienteId, command.VeiculoId, servicoIds);
            await _referenciaGateway.AdicionarAsync(referencia, cancellationToken);
        }
        else
        {
            existente.Atualizar(command.ClienteId, command.VeiculoId, servicoIds);
            await _referenciaGateway.AtualizarAsync(existente, cancellationToken);
        }

        await PublicarValidacaoAsync(command.OrdemServicoId, command.VeiculoId, cliente, servicos, cancellationToken);
        return ResultadoProcessamentoEvento.Processado;
    }

    public async Task<ResultadoProcessamentoEvento> AtualizarStatusAsync(OrdemServicoStatusAlterado evento, CancellationToken cancellationToken = default)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);
        var statusNovo = (StatusOrdemServico)evento.StatusNovo;

        // OS desconhecida (rejeitada ou excluida) ou evento repetido: nada a atualizar
        if (referencia is null || referencia.Status == statusNovo)
        {
            return ResultadoProcessamentoEvento.Ignorado;
        }

        referencia.AtualizarStatus(statusNovo);
        await _referenciaGateway.AtualizarAsync(referencia, cancellationToken);

        if (StatusNotificados.Contains(statusNovo))
        {
            await EnviarNotificacaoStatusAlteradoAsync(referencia, cancellationToken);
        }

        return ResultadoProcessamentoEvento.Processado;
    }

    public async Task<ResultadoProcessamentoEvento> RemoverAsync(OrdemServicoExcluida evento, CancellationToken cancellationToken = default)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (referencia is null)
        {
            return ResultadoProcessamentoEvento.Ignorado;
        }

        await _referenciaGateway.RemoverAsync(referencia, cancellationToken);
        return ResultadoProcessamentoEvento.Processado;
    }

    public async Task<ResultadoProcessamentoEvento> ProcessarPagamentoAprovadoAsync(PagamentoAprovado evento, CancellationToken cancellationToken = default)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        // OS desconhecida (rejeitada ou excluida) ou pagamento repetido: o estoque e debitado uma unica vez por OS
        if (referencia is null || referencia.EstoqueDebitado)
        {
            return ResultadoProcessamentoEvento.Ignorado;
        }

        try
        {
            var servicos = await ObterServicosAsync(referencia.ServicoIds, cancellationToken);

            // Debito dos insumos e marca na referencia na mesma transacao: uma falha no meio nao deixa debito gravado sem a marca,
            // e a nova tentativa da mensagem debita uma unica vez
            await _referenciaGateway.ExecutarEmTransacaoAsync(async () =>
            {
                await _facade.EstoqueUseCases.DebitarEstoqueParaOrdemServicoAsync(servicos, cancellationToken);
                referencia.RegistrarDebitoEstoque();
                await _referenciaGateway.AtualizarAsync(referencia, cancellationToken);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
        {
            // O pagamento ja foi aprovado no Billing: sem nova tentativa nem compensacao, a falha fica registrada para tratamento manual
            RegistrarFalha(OrdemServicoLogEventos.Etapas.EstoqueDebito, evento.OrdemServicoId, exception);
            return ResultadoProcessamentoEvento.Ignorado;
        }

        return ResultadoProcessamentoEvento.Processado;
    }

    private async Task<(Cliente Cliente, IReadOnlyCollection<Servico> Servicos)> ValidarDadosAsync(Guid clienteId, Guid veiculoId, IReadOnlyCollection<Guid> servicoIds, CancellationToken cancellationToken)
    {
        var cliente = await _clienteGateway.ObterPorIdAsync(clienteId, cancellationToken);

        if (cliente is null)
        {
            throw new KeyNotFoundException($"Cliente com ID '{clienteId}' nao encontrado.");
        }

        var veiculo = await _veiculoGateway.ObterPorIdAsync(veiculoId, cancellationToken);

        if (veiculo is null)
        {
            throw new KeyNotFoundException($"Veiculo com ID '{veiculoId}' nao encontrado.");
        }

        if (veiculo.ClienteId != clienteId)
        {
            throw new DomainException("O veiculo informado deve estar vinculado ao cliente da ordem de servico.");
        }

        if (servicoIds.Count == 0)
        {
            throw new DomainException("A ordem de servico deve possuir ao menos um servico.");
        }

        if (servicoIds.Any(servicoId => servicoId == Guid.Empty))
        {
            throw new DomainException("Todos os servicos informados devem possuir identificador valido.");
        }

        var servicos = await ObterServicosAsync(servicoIds, cancellationToken);
        return (cliente, servicos);
    }

    private async Task<IReadOnlyCollection<Servico>> ObterServicosAsync(IEnumerable<Guid> servicoIds, CancellationToken cancellationToken)
    {
        var servicos = new List<Servico>();

        foreach (var servicoId in servicoIds.Distinct())
        {
            var servico = await _servicoGateway.ObterPorIdAsync(servicoId, cancellationToken);

            if (servico is null)
            {
                throw new KeyNotFoundException($"Servico com ID '{servicoId}' nao encontrado.");
            }

            servicos.Add(servico);
        }

        return servicos;
    }

    private async Task PublicarValidacaoAsync(Guid ordemServicoId, Guid veiculoId, Cliente cliente, IReadOnlyCollection<Servico> servicos, CancellationToken cancellationToken)
    {
        // O Management e dono dos servicos e insumos: envia os precos para o Billing montar o orcamento e informa se ha estoque, sem bloquear a OS
        var servicosValidados = servicos
            .Select(servico => new ServicoValidado(servico.Id, servico.Nome, servico.CalcularValor()))
            .ToArray();
        var valorTotal = decimal.Round(servicosValidados.Sum(servico => servico.Valor), 2, MidpointRounding.ToEven);
        var estoqueSuficiente = await _facade.EstoqueUseCases.PossuiDisponibilidadeAsync(servicos, cancellationToken);

        var evento = new OrdemServicoValidada(ordemServicoId, cliente.Id, veiculoId, cliente.Email, estoqueSuficiente, servicosValidados, valorTotal);
        await _facade.EventosPublisher.PublicarAsync(evento, ordemServicoId, cancellationToken);
    }

    private async Task RejeitarAsync(Guid ordemServicoId, OrdemServicoReferencia? existente, Exception motivo, CancellationToken cancellationToken)
    {
        if (existente is not null)
        {
            await _referenciaGateway.RemoverAsync(existente, cancellationToken);
        }

        await _facade.EventosPublisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, motivo.Message), ordemServicoId, cancellationToken);

        RegistrarFalha(
            existente is null ? OrdemServicoLogEventos.Etapas.Abertura : OrdemServicoLogEventos.Etapas.Atualizacao,
            ordemServicoId,
            motivo);
    }

    private void RegistrarFalha(string etapa, Guid ordemServicoId, Exception motivo)
    {
        var tipoFalha = motivo is KeyNotFoundException
            ? OrdemServicoLogEventos.TiposFalha.NaoEncontrado
            : OrdemServicoLogEventos.TiposFalha.RegraNegocio;

        // Falha de regra de negocio nao e erro de aplicacao: registra so o motivo, sem stack trace
        _logger.LogWarning(
            OrdemServicoLogEventos.OsFalha,
            "Falha na etapa {Etapa} da ordem de servico {OrdemServicoId} ({TipoFalha}): {Motivo}",
            etapa,
            ordemServicoId,
            tipoFalha,
            motivo.Message);
    }

    private async Task EnviarNotificacaoStatusAlteradoAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken)
    {
        // Modo degradado: falhas na busca dos dados ou no envio de email (inclusive timeout) não interrompem a atualização de status; só o cancelamento é propagado
        string? emailDestino;
        NotificacaoStatusOrdemServico notificacao;

        try
        {
            var cliente = await _clienteGateway.ObterPorIdAsync(referencia.ClienteId, cancellationToken);
            emailDestino = cliente?.Email;

            if (string.IsNullOrWhiteSpace(emailDestino))
            {
                return;
            }

            notificacao = await CriarNotificacaoAsync(referencia, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Falha ao buscar os dados da ordem de servico {OrdemServicoId} para notificar a alteracao de status.",
                referencia.Id);
            return;
        }

        try
        {
            await _facade.OrdemServicoStatusEmailSender.EnviarStatusAlteradoAsync(notificacao, emailDestino, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                OrdemServicoLogEventos.IntegracaoFalha,
                ex,
                "Falha na integracao {Integracao} durante a operacao {Operacao} da ordem de servico {OrdemServicoId} com status {StatusNovo}.",
                OrdemServicoLogEventos.Integracoes.Resend,
                OrdemServicoLogEventos.Operacoes.NotificacaoStatus,
                referencia.Id,
                OrdemServicoLogEventos.StatusParaLog(referencia.Status));
        }
    }

    private async Task<NotificacaoStatusOrdemServico> CriarNotificacaoAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken)
    {
        var servicos = new List<string>(referencia.ServicoIds.Count);

        foreach (var servicoId in referencia.ServicoIds)
        {
            var servico = await _servicoGateway.ObterPorIdAsync(servicoId, cancellationToken);

            if (servico is not null)
            {
                servicos.Add(servico.Nome);
            }
        }

        return new NotificacaoStatusOrdemServico
        {
            OrdemServicoId = referencia.Id,
            ClienteId = referencia.ClienteId,
            VeiculoId = referencia.VeiculoId,
            StatusNovo = referencia.Status,
            Servicos = servicos
        };
    }
}
