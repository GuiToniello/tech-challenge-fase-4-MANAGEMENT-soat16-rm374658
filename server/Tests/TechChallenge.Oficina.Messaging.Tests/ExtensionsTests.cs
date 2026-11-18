using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechChallenge.Oficina.Messaging.Configuration;
using TechChallenge.Oficina.Messaging.Features.Eventos;
using TechChallenge.Oficina.Messaging.Sqs;
using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using Xunit;

namespace TechChallenge.Oficina.Messaging.Tests;

public sealed class ExtensionsTests
{
    [Fact]
    public void AddInfraMessaging_DeveRegistrarPublicadorClienteSqsEConsumidor()
    {
        var services = new ServiceCollection();
        var settings = new SqsSettings
        {
            FilaConsumo = "techchallenge-oficina-management.fifo",
            FilasPublicacao = ["techchallenge-oficina-os-service.fifo", "techchallenge-oficina-billing.fifo"],
            ServiceUrl = "http://localhost:9324"
        };

        services.AddInfraMessaging(settings);

        Assert.Contains(services, descritor => descritor.ServiceType == typeof(SqsSettings) && descritor.ImplementationInstance == settings);
        Assert.Contains(services, descritor => descritor.ServiceType == typeof(IAmazonSQS) && descritor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descritor => descritor.ServiceType == typeof(ISqsClient) && descritor.ImplementationType == typeof(SqsClientAdapter));
        Assert.Contains(services, descritor => descritor.ServiceType == typeof(IEventosPublisher) && descritor.ImplementationType == typeof(EventosPublisher) && descritor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descritor => descritor.ServiceType == typeof(IHostedService) && descritor.ImplementationType == typeof(SqsConsumidorHostedService));
    }

    [Fact]
    public void AddInfraMessaging_NaoDeveRegistrarConsumidor_QuandoFilaDeConsumoNaoInformada()
    {
        var services = new ServiceCollection();

        services.AddInfraMessaging(new SqsSettings { FilasPublicacao = ["techchallenge-oficina-os-service.fifo"] });

        Assert.DoesNotContain(services, descritor => descritor.ServiceType == typeof(IHostedService));
        Assert.Contains(services, descritor => descritor.ServiceType == typeof(IEventosPublisher));
    }
}
