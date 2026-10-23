using OpenCode.Sdk;
using OpenCode.Sdk.Models;

sealed partial class OpenCodeBackend
{
    public sealed class SessionReleasedException(string task) : HarnessException(
        $"{task}: the session was released when the task finished; result.json is final");

    async Task<WorkerReport> CollectCoreAsync(WorkerRef worker, CancellationToken ct)
    {
        var session = (await HostAsync(worker, ct)).Client.Sessions.GetSessionClient(worker.Id);
        SessionInfo info;
        List<SessionMessageAssistant> assistants;
        List<TokenTotals> calls;
        SessionMessageIdle? idle;
        string? text;
        var reads = 0;
        var complete = false;
        while (true)
        {
            var response = await session.GetAsync(OpenCodeRequestOptions.NoThrow, ct);
            if (response.Status == 404) throw new SessionReleasedException(worker.TaskId);
            if (response.IsError) throw new HarnessException($"session {worker.Id} collection failed: HTTP {response.Status} {response.Error?.Tag}");
            info = response.Session;
            assistants = [];
            calls = [];
            idle = null;
            var current = true;
            await foreach (var message in session.EnumerateMessagesAsync(new SessionMessageListRequest { Order = ListOrder.Descending }, cancellationToken: ct))
            {
                if (message is SessionMessageAssistant { Tokens: { } tokens })
                    calls.Add(new() { Input = tokens.Input, CacheRead = tokens.Cache.Read,
                        Output = tokens.Output, Reasoning = tokens.Reasoning });
                if (message.Id is not null && message.Id == worker.Turn) current = false;
                if (!current) continue;
                if (message is SessionMessageAssistant assistant) assistants.Add(assistant);
                if (message is SessionMessageIdle marker && idle is null) idle = marker;
            }
            var final = assistants.FirstOrDefault(x => x.Error is null);
            text = final is null ? null : string.Join("\n\n", final.Content.OfType<SessionMessageAssistantText>().Select(x => x.Text));
            complete = interrupts.ContainsKey(worker.Id) || info.Outcome != SessionInfoOutcome.Succeeded || (!string.IsNullOrWhiteSpace(text) && assistants.All(x => x.Tokens is not null));
            if (complete || ++reads >= Constants.SettleReads) break;
            await Task.Delay(Constants.SettleReadDelay, ct);
        }
        calls.Reverse();
        return new(info.Outcome?.ToString().ToLowerInvariant() ?? "lost", text, new TokenTotals
        {
            Input = info.Tokens.Input, Output = info.Tokens.Output, Reasoning = info.Tokens.Reasoning,
            CacheRead = info.Tokens.Cache.Read, CacheWrite = info.Tokens.Cache.Write,
        }, info.Cost, assistants.All(x => x.Tokens is not null),
            assistants.Select(x => OpenCodeModels.From(x.Model).ToString()).Distinct().ToList(),
            idle?.Error?.Message ?? assistants.FirstOrDefault(x => x.Error is not null)?.Error?.Message, complete,
            ContextWindow: worker.State["contextWindow"]?.GetValue<long>(), Calls: calls);
    }
}
