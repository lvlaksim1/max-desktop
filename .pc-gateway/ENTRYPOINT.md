# PC Gateway Client Entrypoint

Protocol: `pc-runner-gateway`  
Client protocol version: `1.0.0`  
Gateway: `lvlaksim1/pc-runner-gateway`  
Standard transport: `github-issue-queue`

This file is the first document an engineer or AI agent must read before changing or using this repository's PC gateway integration.

## Non-negotiable rules

1. This repository is a **caller**, not a runner owner. It must not register the trusted Windows runner directly.
2. PC execution goes only through `lvlaksim1/pc-runner-gateway`.
3. Never send inline PowerShell or arbitrary command text in a request.
4. Project code that may run on the PC belongs under `.pc-gateway/tasks/`.
5. `repo.powershell` requests always identify an exact 40-character commit SHA and an allowed script path.
6. Project task scripts are invoked with exactly `GatewayRequestPath` and `GatewayResultPath`.
7. The standard transport creates a GitHub Issue in the private gateway repository. It does not require GitHub-hosted Actions in the caller.
8. A successfully created Issue means only that the request is queued. PC execution completes later on `pc-gateway-primary`.
9. Completion is recorded by the gateway as a result comment on the request Issue, the Issue is closed, and the full result/evidence remains in the trusted PC gateway ledger. No GitHub Actions artifact is created.
10. `request_id` is an idempotency key. Do not intentionally reuse it for a new execution.
11. Credentials never belong in request payloads, repository files, local evidence or logs.
12. Managed Client Kit files should be changed only through a Client Kit upgrade. Project-specific work belongs in `.pc-gateway/tasks/`.

## How to submit

For an engineer or local automation, use:

~~~powershell
.\.pc-gateway\Submit-PCGatewayRequest.ps1 -Task host.ping
~~~

For a live connection test:

~~~powershell
.\.pc-gateway\Test-PCGatewayConnection.ps1
~~~

An AI agent with authenticated GitHub access may create the same standard Issue directly, but it must follow `.pc-gateway/CONTRACT.md` exactly.

Read `.pc-gateway/CONTRACT.md` for the complete request/result contract and `.pc-gateway/client.json` for machine-readable settings.
