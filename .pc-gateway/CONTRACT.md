# PC Gateway Client Contract

## Identity

- Gateway repository: `lvlaksim1/pc-runner-gateway`
- Protocol: `pc-runner-gateway`
- Supported protocol version: `1.0.0`
- Standard transport: GitHub Issue queue in the private gateway repository
- Request title prefix: `PC Gateway Request: `
- Project script root: `.pc-gateway/tasks/`

## Transport

The standard Client Kit does **not** use a caller GitHub Actions workflow.

A request is one new Issue in `lvlaksim1/pc-runner-gateway`:

- title: `PC Gateway Request: <request_id>`
- body: the request envelope as a JSON object

Opening that Issue causes the gateway's own workflow to queue on `pc-gateway-primary`. Ordinary Issues whose title does not use the exact prefix are ignored by the gateway execution job.

A local sender may authenticate with:

- the `-Token` argument;
- `PC_GATEWAY_DISPATCH_TOKEN`; or
- an authenticated GitHub CLI session.

The credential needs access to create/read Issues in the private gateway repository. It must never be written into the request body.

Do not use the default `GITHUB_TOKEN` from another repository workflow as the standard sender. GitHub suppresses most workflow-triggering events created by that token. Use a user/PAT/GitHub App credential or authenticated connected GitHub access.

## Request envelope

Every remote request contains:

- `protocol`
- `protocol_version`
- `request_id`
- `source_repository`
- `source_ref`
- `task`
- `args`
- `requested_at`

For `repo.powershell`:

- `source_ref` must be the exact 40-character commit SHA;
- `args.script_path` must point to an allowed `.ps1` under `.pc-gateway/tasks/`.

The gateway still supports the older `repository_dispatch / pc_task` protocol for compatibility, but it is not the standard Client Kit transport.

## Project task interface

Every executable project task must accept:

~~~powershell
param(
    [string]$GatewayRequestPath,
    [string]$GatewayResultPath
)
~~~

Read project-specific inputs from `GatewayRequestPath`. Write a compact JSON object to `GatewayResultPath` and use the process exit code to indicate success/failure.

## Result

For the Issue transport the gateway:

1. retains the full result and available evidence in the trusted PC request ledger;
2. writes a compact `PC Gateway result` comment to the request Issue;
3. closes the request Issue.

The gateway deliberately does not publish GitHub Actions artifacts. Detailed evidence is retrieved from the trusted PC ledger when needed.

## Protocol compatibility

The gateway rejects unsupported versions with `CLIENT_PROTOCOL_VERSION_UNSUPPORTED`. Upgrade the managed Client Kit files instead of bypassing the version check.

## Security boundary

A repository granted `repo.powershell` is trusted to execute its approved committed task code under the Windows account used by the gateway runner. Do not submit unreviewed commit SHAs.

The Issue transport authenticates the GitHub identity/token that can write to the **private gateway repository**. The `source_repository` field remains a policy selector rather than a cryptographic proof of repository origin. Therefore Issue-write access to the gateway must be limited to trusted operators/automation. Exact commit pinning and allowed script roots still constrain the code that can execute.
