using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using TechChallenge.Oficina.UseCases.Features.Clientes.Commands;
using TechChallenge.Oficina.UseCases.Features.Clientes.ViewModels;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.Insumos.Commands;
using TechChallenge.Oficina.UseCases.Features.Insumos.ViewModels;
using TechChallenge.Oficina.UseCases.Features.Servicos.Commands;
using TechChallenge.Oficina.UseCases.Features.Servicos.ViewModels;
using TechChallenge.Oficina.UseCases.Features.Veiculos.Commands;
using TechChallenge.Oficina.UseCases.Features.Veiculos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Integration.Tests.Infrastructure;

/// <summary>
/// Monta os cadastros do Management pelos controllers e simula os eventos do OS Service (dados JSON em camelCase, como no envelope SQS).
/// </summary>
internal static class CenarioOrdemServico
{
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    public static async Task<(Guid ClienteId, Guid VeiculoId, Guid InsumoId, Guid ServicoId)> CriarCadastrosAsync(
        IntegrationTestFixture fixture,
        int quantidadeDisponivel = 20,
        int quantidadeNoServico = 4)
    {
        var clienteId = await CriarClienteAsync(fixture, "529.982.247-25", "cliente@email.com");
        var veiculoId = await CriarVeiculoAsync(fixture, clienteId, "ABC1D23", "12345678901");
        var insumoId = await CriarInsumoAsync(fixture, quantidadeDisponivel);
        var servicoId = await CriarServicoAsync(fixture, insumoId, quantidadeNoServico);

        return (clienteId, veiculoId, insumoId, servicoId);
    }

    public static async Task<Guid> CriarClienteAsync(IntegrationTestFixture fixture, string identificacao, string? email)
    {
        var resultado = Assert.IsType<CreatedAtRoute<ClienteViewModel>>(await fixture.CriarClientesEndpoints().Post(
            new CriarClienteCommand { NomeCompleto = "Cliente Integracao", Identificacao = identificacao, Email = email },
            CancellationToken.None));

        return resultado.Value!.Id;
    }

    public static async Task<Guid> CriarVeiculoAsync(IntegrationTestFixture fixture, Guid clienteId, string placa, string renavam)
    {
        var resultado = Assert.IsType<CreatedAtRoute<VeiculoViewModel>>(await fixture.CriarVeiculosEndpoints().Post(
            new CriarVeiculoCommand { Placa = placa, Marca = "Ford", Modelo = "Ka", Ano = 2022, Renavam = renavam, ClienteId = clienteId },
            CancellationToken.None));

        return resultado.Value!.Id;
    }

    public static async Task<Guid> CriarInsumoAsync(IntegrationTestFixture fixture, int quantidadeDisponivel)
    {
        var resultado = Assert.IsType<CreatedAtRoute<InsumoViewModel>>(await fixture.CriarInsumosEndpoints().Post(
            new CriarInsumoCommand { Nome = "Vela de Ignicao", Fabricante = "NGK", QuantidadeDisponivel = quantidadeDisponivel, ValorUnitario = 15m },
            CancellationToken.None));

        return resultado.Value!.Id;
    }

    public static async Task<Guid> CriarServicoAsync(IntegrationTestFixture fixture, Guid insumoId, int quantidade)
    {
        var resultado = Assert.IsType<CreatedAtRoute<ServicoViewModel>>(await fixture.CriarServicosEndpoints().Post(
            new CriarServicoCommand
            {
                Nome = "Troca de Vela",
                Descricao = "Substituicao das velas de ignicao",
                ItensServico = [new ItemServicoCommand { InsumoId = insumoId, Quantidade = quantidade }]
            },
            CancellationToken.None));

        return resultado.Value!.Id;
    }

    public static Task<ResultadoProcessamentoEvento> AbrirOrdemServicoAsync(IntegrationTestFixture fixture, Guid ordemServicoId, Guid clienteId, Guid veiculoId, params Guid[] servicoIds)
        => ProcessarAsync(fixture, new OrdemServicoAberta(ordemServicoId, clienteId, veiculoId, servicoIds));

    public static Task<ResultadoProcessamentoEvento> AlterarStatusAsync(IntegrationTestFixture fixture, Guid ordemServicoId, Guid clienteId, int statusAnterior, int statusNovo)
        => ProcessarAsync(fixture, new OrdemServicoStatusAlterado(ordemServicoId, clienteId, statusAnterior, statusNovo));

    public static Task<ResultadoProcessamentoEvento> ExcluirOrdemServicoAsync(IntegrationTestFixture fixture, Guid ordemServicoId)
        => ProcessarAsync(fixture, new OrdemServicoExcluida(ordemServicoId));

    private static Task<ResultadoProcessamentoEvento> ProcessarAsync<TEvento>(IntegrationTestFixture fixture, TEvento evento)
        => fixture.CriarEventosController().ProcessarAsync(typeof(TEvento).Name, JsonSerializer.Serialize(evento, OpcoesJson), CancellationToken.None);
}
