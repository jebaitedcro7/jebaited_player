# Diagnostics endpoint (optional, not deployed)

The player keeps redacted error reports locally (`logs\reports`) and only sends them when the user allowed it
**and** an endpoint is configured. Until then, users can send them themselves as a GitHub issue (Settings → About).

To enable automatic sending (owner's decision, free Cloudflare plan, an account is needed):
1. Create a Cloudflare Worker from `worker.js` and a KV namespace bound as `REPORTS`.
2. Put the worker's https URL into `ErrorReporting.Endpoint` (src/JebaitedPlayer.App/Services/ErrorReporting.cs) and release a new version.
3. Reports are kept for 90 days; at most 200 per day are accepted. No IP address or headers are stored.
