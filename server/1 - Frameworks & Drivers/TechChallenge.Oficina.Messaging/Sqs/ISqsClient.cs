namespace TechChallenge.Oficina.Messaging.Sqs;

// Abstracao minima do Amazon SQS usada pelo publicador e pelo consumidor (facilita os testes sem o SDK).
public interface ISqsClient
{
    Task<string> GetQueueUrlAsync(string queueName, CancellationToken cancellationToken = default);

    Task SendMessageAsync(string queueUrl, string body, string tipo, string messageGroupId, string messageDeduplicationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<MensagemSqs>> ReceiveMessagesAsync(string queueUrl, int maxNumberOfMessages, int waitTimeSeconds, CancellationToken cancellationToken = default);

    Task DeleteMessageAsync(string queueUrl, string receiptHandle, CancellationToken cancellationToken = default);
}
