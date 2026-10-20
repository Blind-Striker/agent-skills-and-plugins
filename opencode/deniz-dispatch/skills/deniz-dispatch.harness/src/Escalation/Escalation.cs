// An escalated ask the supervisor holds until the operator decides.
sealed record Escalation(PendingAsk Ask, DateTimeOffset Queued);
