## Best Practices Summary

1. **Always use health checks** - Configure readiness and liveness checks for all components
2. **Bind settings from configuration** - Never hard-code hostnames, ports, or connection strings
3. **Use Akka.Management for multi-node** - Don't use static seed nodes for clusters with >1 replica
4. **Configure TLS for production** - Always use TLS in production environments
5. **Separate actor logic from configuration** - Keep actors pure and configuration in extension methods
6. **Use Petabridge.Cmd** - Essential for debugging and managing clusters
7. **Test with multiple replicas** - Always test with `Replicas > 1` to catch clustering issues
8. **Monitor persistence health** - Configure health checks for journal and snapshot stores
