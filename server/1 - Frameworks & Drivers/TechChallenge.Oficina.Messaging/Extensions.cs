using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using TechChallenge.Oficina.Messaging.Configuration;
using TechChallenge.Oficina.Messaging.Features.Eventos;
using TechChallenge.Oficina.Messaging.Sqs;
using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;

namespace TechChallenge.Oficina.Messaging;

public static class Extensions
{
    public static IServiceCollection AddInfraMessaging(this IServiceCollection services, SqsSettings sqsSettings)
    {
        services.AddSingleton(sqsSettings);
        // Credenciais pela cadeia padrao do SDK (AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY ou role) e regiao por AWS_REGION
        services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(CriarConfiguracao(sqsSettings)));
        services.AddSingleton<ISqsClient, SqsClientAdapter>();
        services.AddSingleton<IEventosPublisher, EventosPublisher>();

        if (!string.IsNullOrWhiteSpace(sqsSettings.FilaConsumo))
        {
            services.AddHostedService<SqsConsumidorHostedService>();
        }

        return services;
    }

    private static AmazonSQSConfig CriarConfiguracao(SqsSettings sqsSettings)
    {
        var configuracao = new AmazonSQSConfig();

        if (!string.IsNullOrWhiteSpace(sqsSettings.ServiceUrl))
        {
            // Endpoint local (ElasticMQ) no lugar do endpoint da AWS
            configuracao.ServiceURL = sqsSettings.ServiceUrl;
        }

        return configuracao;
    }
}
