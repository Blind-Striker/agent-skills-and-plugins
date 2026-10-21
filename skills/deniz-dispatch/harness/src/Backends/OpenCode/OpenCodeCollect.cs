using OpenCode.Sdk;
using OpenCode.Sdk.Models;

sealed partial class OpenCodeBackend
{
    async Task<WorkerReport> CollectCoreAsync(WorkerRef worker, CancellationToken ct)
    {
        var session = (await HostAsync(worker, ct)).Client.Sessions.GetSessionClient(worker.Id);
        SessionInfo info;
        List<SessionMessageAssistant> assistants;
        SessionMessageIdle? idle;
        string? text;
        var reads = 0;
        var complete = false;
        while (true)
        {
            info = (await session.GetAsync(cancellationToken: ct)).Session;
            assistants = [];
            idle = null;
            await foreach (var message in session.EnumerateMessagesAsync(new SessionMessageListRequest { Order = ListOrder.Descending }, cancellationToken: ct))
            {
                if (message.Id is not null && message.Id == worker.Turn) break;
                if (message is SessionMessageAssistant assistant) assistants.Add(assistant);
                if (message is SessionMessageIdle marker && idle is null) idle = marker;
            }
            var final = assistants.FirstOrDefault(x => x.Error is null);
            text = final is null ? null : string.Join("\n\n", final.Content.OfType<SessionMessageAssistantText>().Select(x => x.Text));
            complete = interrupts.ContainsKey(worker.Id) || info.Outcome != SessionInfoOutcome.Succeeded || (!string.IsNullOrWhiteSpace(text) && assistants.All(x => x.Tokens is not null));
            if (complete || ++reads >= Constants.SettleReads) break;
            await Task.Delay(Constants.SettleReadDelay, ct);
        }
        return new(info.Outcome?.ToString().ToLowerInvariant() ?? "lost", text, new TokenTotals
        {
            Input = info.Tokens.Input, Output = info.Tokens.Output, Reasoning = info.Tokens.Reasoning,
            CacheRead = info.Tokens.Cache.Read, CacheWrite = info.Tokens.Cache.Write,
        }, info.Cost, assistants.All(x => x.Tokens is not null),
            assistants.Select(x => OpenCodeModels.From(x.Model).ToString()).Distinct().ToList(),
            idle?.Error?.Message ?? assistants.FirstOrDefault(x => x.Error is not null)?.Error?.Message, complete);
    }
}
