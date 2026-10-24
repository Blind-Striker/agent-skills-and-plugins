static class Constants
{
    public const int SchemaVersion = 2;
    public const string HarnessVersion = "1.0.0";
    // The upstream release the SDK package is generated from.
    public const string PinnedUpstream = "2.1.4";
    public const string DefaultPersona = "build";
    public const int DefaultMaxParallel = 3;
    public const int DefaultTimeoutMinutes = 60;
    public static readonly TimeSpan BusRead = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(30);
    public const int WatchdogFailureLimit = 3;
    public static readonly TimeSpan RouteWait = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan VerifyTimeout = TimeSpan.FromMinutes(30);
    public const int SettleReads = 15;
    public static readonly TimeSpan SettleReadDelay = TimeSpan.FromSeconds(2);
    public static readonly string[] Kinds = ["code", "review", "research", "mechanical", "structured"];
}
