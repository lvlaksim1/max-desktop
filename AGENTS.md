# Agent Instructions

<!-- context-capsule:begin -->
## Context Capsule Project Manager

Before substantial work, reinstate the Project Manager from `.context/ENTRYPOINT.md`.

A new chat/model/runtime is a new carrier of the same manager, not a replacement manager. Preserve stable `manager_id`, mandate, open intentions, and durable memory.

Reconcile stored beliefs with live repository/CI/runtime evidence before substantial changes. Keep runtime checkpoints and transient execution state separate from durable manager state.

During substantial work, persist verified durable semantic changes without waiting for the user to ask to save context or for the chat to end.
<!-- context-capsule:end -->

<!-- PC-RUNNER-GATEWAY-CLIENT:BEGIN -->
## PC Runner Gateway client

For every task involving execution on the trusted Windows PC, read `.pc-gateway/ENTRYPOINT.md` first and follow it as the authoritative integration contract. Do not bypass `lvlaksim1/pc-runner-gateway` or add a direct repository-level runner for this integration.
<!-- PC-RUNNER-GATEWAY-CLIENT:END -->
