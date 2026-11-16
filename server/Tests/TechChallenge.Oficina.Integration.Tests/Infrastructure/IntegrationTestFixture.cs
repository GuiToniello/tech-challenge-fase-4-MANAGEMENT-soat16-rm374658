using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TechChallenge.Oficina.Adapters.Features.Clientes;
using TechChallenge.Oficina.Adapters.Features.Insumos;
using TechChallenge.Oficina.Adapters.Features.Orcamentos;
using TechChallenge.Oficina.Adapters.Features.Servicos;
using TechChallenge.Oficina.Adapters.Features.Veiculos;
using TechChallenge.Oficina.Controllers.Features.Clientes;
using TechChallenge.Oficina.Controllers.Features.Eventos;
using TechChallenge.Oficina.Controllers.Features.Insumos;
using TechChallenge.Oficina.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Controllers.Features.Servicos;
using TechChallenge.Oficina.Controllers.Features.Veiculos;
using TechChallenge.Oficina.DB.Data.Context;
using TechChallenge.Oficina.DB.Data.Features.Clientes;
using TechChallenge.Oficina.DB.Data.Features.Insumos;
using TechChallenge.Oficina.DB.Data.Features.Orcamentos;
using TechChallenge.Oficina.DB.Data.Features.OrdensServico;
using TechChallenge.Oficina.DB.Data.Features.Servicos;
using TechChallenge.Oficina.DB.Data.Features.Veiculos;
using TechChallenge.Oficina.UseCases.Features.Clientes.Mappings;
using TechChallenge.Oficina.UseCases.Features.Clientes.UseCases;
using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Mappings;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.UseCases.Features.Servicos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Veiculos.UseCases;

namespace TechChallenge.Oficina.Integration.Tests.Infrastructure;

/// <summary>
/// Fixture de integração: DI real com EF Core InMemory e mock apenas das integrações externas
/// (publicação de eventos no SQS e envio de emails pelo Resend).
/// Cada instância possui banco de dados isolado (nome único por GUID).
/// </summary>
public sealed class IntegrationTestFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;

    public Mock<IEventosPublisher> EventosPublisherMock { get; } = new();
    public Mock<IOrcamentoEmailSender> OrcamentoEmailSenderMock { get; } = new();
    public Mock<IOrdemServicoStatusEmailSender> StatusEmailSenderMock { get; } = new();

    public IntegrationTestFixture()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<OficinaDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddScoped<IClienteGateway, ClienteGateway>();
        services.AddScoped<IInsumoGateway, InsumoGateway>();
        services.AddScoped<IOrcamentoGateway, OrcamentoGateway>();
        services.AddScoped<IOrdemServicoReferenciaGateway, OrdemServicoReferenciaGateway>();
        services.AddScoped<IServicoGateway, ServicoGateway>();
        services.AddScoped<IVeiculoGateway, VeiculoGateway>();

        services.AddSingleton(EventosPublisherMock.Object);
        services.AddSingleton(OrcamentoEmailSenderMock.Object);
        services.AddSingleton(StatusEmailSenderMock.Object);

        services.AddAutoMapper(_ => { }, typeof(ClienteProfile).Assembly, typeof(OrcamentoProfile).Assembly);
        services.AddScoped<IClienteUseCases, ClienteUseCases>();
        services.AddScoped<IInsumoUseCases, InsumoUseCases>();
        services.AddScoped<IEstoqueUseCases, EstoqueUseCases>();
        services.AddScoped<IOrcamentoUseCasesFacade, OrcamentoUseCasesFacade>();
        services.AddScoped<IOrcamentoUseCases, OrcamentoUseCases>();
        services.AddScoped<IOrdemServicoReferenciaUseCasesFacade, OrdemServicoReferenciaUseCasesFacade>();
        services.AddScoped<IOrdemServicoReferenciaUseCases, OrdemServicoReferenciaUseCases>();
        services.AddScoped<IServicoUseCases, ServicoUseCases>();
        services.AddScoped<IVeiculoUseCases, VeiculoUseCases>();
        services.AddScoped<IClienteAdapter, ClienteAdapter>();
        services.AddScoped<ClienteController>();
        services.AddScoped<IServicoAdapter, ServicoAdapter>();
        services.AddScoped<ServicoController>();
        services.AddScoped<IOrcamentosAdapter, OrcamentosAdapter>();
        services.AddScoped<OrcamentosController>();
        services.AddScoped<IEventosController, EventosController>();
        services.AddScoped<IVeiculoAdapter, VeiculoAdapter>();
        services.AddScoped<VeiculoController>();
        services.AddScoped<InsumoController>();
        services.AddScoped<IInsumoAdapter, InsumoAdapter>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
    }

    private T Obter<T>() where T : notnull
        => _scope.ServiceProvider.GetRequiredService<T>();

    public ClienteController CriarClientesEndpoints()
        => Obter<ClienteController>();

    public InsumoController CriarInsumosEndpoints()
        => Obter<InsumoController>();

    public VeiculoController CriarVeiculosEndpoints()
        => Obter<VeiculoController>();

    public ServicoController CriarServicosEndpoints()
        => Obter<ServicoController>();

    public OrcamentosController CriarOrcamentosEndpoints()
        => Obter<OrcamentosController>();

    public IEventosController CriarEventosController()
        => Obter<IEventosController>();

    public OficinaDbContext ObterContexto()
        => Obter<OficinaDbContext>();

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }
}
