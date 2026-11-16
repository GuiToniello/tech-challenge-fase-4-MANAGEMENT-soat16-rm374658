namespace TechChallenge.Oficina.Messaging.Sqs;

public sealed record MensagemSqs(string MessageId, string ReceiptHandle, string Body);
