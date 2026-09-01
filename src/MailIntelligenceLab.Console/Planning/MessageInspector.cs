using MailIntelligenceLab.Ports;

namespace MailIntelligenceLab.Planning;

public sealed class MessageInspector(IEmailProvider emailProvider)
{
    public async Task<IReadOnlyList<MessageSummary>> InspectAsync(string senderAddress)
    {
        var messages = await emailProvider.ListFromSenderAsync("inbox", senderAddress);
        return messages.OrderByDescending(m => m.ReceivedDateTime).ToList();
    }
}