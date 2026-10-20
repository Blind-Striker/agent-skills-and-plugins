// A fresh server, or a fresh location on it, answers 503 while it starts or while the location's
// plugins activate (upstream server/src/process.ts, ServiceUnavailableError). That means "not
// yet", so the listings a command makes at startup poll under a deadline instead of failing.
static class Ready
{
    public static bool IsNotReady(OpenCodeApiException ex) => ex.Status == 503;

    public static async Task<T> PollAsync<T>(Func<Task<T>> call, TimeSpan wait, string what, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            try
            {
                return await call();
            }
            catch (OpenCodeApiException ex) when (IsNotReady(ex))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new NotReadyException($"{what}: the server still answered {ex.Status} after {wait.TotalSeconds:0} s: {ex.Message}");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
