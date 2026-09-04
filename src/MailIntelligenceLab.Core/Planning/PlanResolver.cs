using MailIntelligenceLab.Models;
using MailIntelligenceLab.Ports;

namespace MailIntelligenceLab.Planning;

public sealed class PlanResolver(IEmailProvider emailProvider)
{
    // Sequential by design: a plan marks a handful of senders, so one request
    // each is fast enough that bounded parallelism would be optimising an
    // unmeasured cost.
    public async Task<List<SenderResolution>> ResolveAsync(
        IEnumerable<ActionPlanRow> markedRows,
        DateTime freezeBoundUtc)
    {
        var resolutions = new List<SenderResolution>();

        foreach (var row in markedRows)
        {
            try
            {
                int resolvedCount = await emailProvider.CountFromSenderAsync(
                    "inbox", row.SenderAddress, freezeBoundUtc);

                resolutions.Add(new SenderResolution(
                    row.SenderAddress, row.MessageCount, resolvedCount, null));
            }
            catch (Exception ex)
            {
                resolutions.Add(new SenderResolution(
                    row.SenderAddress, row.MessageCount, 0, ex.Message));
            }
        }

        return resolutions;
    }
}