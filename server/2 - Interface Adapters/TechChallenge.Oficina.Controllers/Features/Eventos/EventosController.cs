using System.Text.Json;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Commands;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

namespace TechChallenge.Oficina.Controllers.Features.Eventos
{
    // Converte os dados do envelope (camelCase) no contrato do evento e aciona o caso de uso. Excecoes sao propagadas para a mensagem voltar a fila (nova tentativa e, depois, DLQ).
    public sealed class EventosController : IEventosController
    {
        private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

        private readonly IOrdemServicoReferenciaUseCases _ordemServicoReferenciaUseCases;

        public EventosController(IOrdemServicoReferenciaUseCases ordemServicoReferenciaUseCases)
        {
            _ordemServicoReferenciaUseCases = ordemServicoReferenciaUseCases;
        }

        public async Task<ResultadoProcessamentoEvento> ProcessarAsync(string tipo, string dadosJson, CancellationToken cancellationToken)
        {
            return tipo switch
            {
                nameof(OrdemServicoAberta) => await RegistrarAsync(Desserializar<OrdemServicoAberta>(tipo, dadosJson), cancellationToken),
                nameof(OrdemServicoAtualizada) => await RegistrarAsync(Desserializar<OrdemServicoAtualizada>(tipo, dadosJson), cancellationToken),
                nameof(OrdemServicoStatusAlterado) => await _ordemServicoReferenciaUseCases.AtualizarStatusAsync(Desserializar<OrdemServicoStatusAlterado>(tipo, dadosJson), cancellationToken),
                nameof(OrdemServicoExcluida) => await _ordemServicoReferenciaUseCases.RemoverAsync(Desserializar<OrdemServicoExcluida>(tipo, dadosJson), cancellationToken),
                nameof(PagamentoAprovado) => await _ordemServicoReferenciaUseCases.ProcessarPagamentoAprovadoAsync(Desserializar<PagamentoAprovado>(tipo, dadosJson), cancellationToken),
                // Eventos publicados pelo proprio Management ou de outros tipos nao interessam a este servico
                _ => ResultadoProcessamentoEvento.Ignorado
            };
        }

        private Task<ResultadoProcessamentoEvento> RegistrarAsync(OrdemServicoAberta evento, CancellationToken cancellationToken)
        {
            return _ordemServicoReferenciaUseCases.RegistrarAsync(CriarCommand(evento.OrdemServicoId, evento.ClienteId, evento.VeiculoId, evento.ServicoIds), cancellationToken);
        }

        private Task<ResultadoProcessamentoEvento> RegistrarAsync(OrdemServicoAtualizada evento, CancellationToken cancellationToken)
        {
            return _ordemServicoReferenciaUseCases.RegistrarAsync(CriarCommand(evento.OrdemServicoId, evento.ClienteId, evento.VeiculoId, evento.ServicoIds), cancellationToken);
        }

        private static RegistrarOrdemServicoReferenciaCommand CriarCommand(Guid ordemServicoId, Guid clienteId, Guid veiculoId, IReadOnlyCollection<Guid>? servicoIds)
        {
            return new RegistrarOrdemServicoReferenciaCommand
            {
                OrdemServicoId = ordemServicoId,
                ClienteId = clienteId,
                VeiculoId = veiculoId,
                ServicoIds = servicoIds ?? []
            };
        }

        private static TEvento Desserializar<TEvento>(string tipo, string dadosJson) where TEvento : class
        {
            return JsonSerializer.Deserialize<TEvento>(dadosJson, OpcoesJson)
                ?? throw new JsonException($"Os dados do evento {tipo} nao foram informados.");
        }
    }
}
