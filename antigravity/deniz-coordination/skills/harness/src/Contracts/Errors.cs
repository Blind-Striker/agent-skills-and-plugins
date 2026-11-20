class HarnessException(string message) : Exception(message);

// The server, or a location on it, still answered 503 when a startup deadline passed.
sealed class NotReadyException(string message) : HarnessException(message);
