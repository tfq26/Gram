# Gram Connector Host

Runs approved generated connectors outside the main Gram process. It binds to
`127.0.0.1:7340` by default and requires `GRAM_CONNECTOR_HOST_SECRET` on every
request.

```bash
GRAM_CONNECTOR_HOST_SECRET="<installation-secret>" \
dotnet run --project clients/Gram.ConnectorHost/Gram.ConnectorHost.csproj
```

The Gram backend must receive the same secret and may override the endpoint with
`GRAM_CONNECTOR_HOST_URL`. The host accepts package IDs only; it does not accept
arbitrary assembly paths, shell commands, or filesystem paths.

Current isolation is process-level. Production deployment must additionally apply
OS sandboxing, outbound-domain allowlists, resource limits, and a credential broker
before generated connectors receive provider credentials.

Each action runs in a fresh `Gram.ConnectorWorker` process. Set
`GRAM_CONNECTOR_WORKER_PATH` when the worker assembly is not colocated with the
host. The host clears inherited environment variables, monitors timeout, memory,
CPU time, and output size, and kills the complete worker process tree on failure.

Network access remains denied-by-policy until the domain allowlist proxy is
available. Connectors declaring domains are marked `proxy-required`; they are not
silently granted unrestricted egress.
