# Context Capsule entrypoint

## Стандарт пакетной разработки (OWNER-BATCH-EXECUTION-2026-10-30)

Owner-approved development execution: model plans one complete bounded stage; DOMKAT executes the predefined operations as a local batch without per-command model round trips; computer returns one meaningful results/diagnostic summary. Keep about one second between consecutive GUI-control actions, checking UI readiness. Do not add artificial sleeps to PowerShell, CMD, Git, file, build or test operations. On an unexpected error the local batch stops only the affected unsafe work and returns diagnostics; the model, not the batch, evaluates risk and independently decides whether to repair, adjust, skip optional work or escalate to Owner. Do not automatically abandon all development; ask Owner when authority, significant loss, irreversibility or uncertainty requires it. Preserve the existing identity, state integrity, per-branch authority and constraints.

## Local-first workspace directive (Owner, 2026-10-30)

Default to the authorized local Git worktree on DOMKAT under D:\Dev, with this agent's capsule alongside its repository. GitHub is the versioned remote and verified fallback; justified direct GitHub development remains allowed. Check the correct authority branches, local uncommitted changes and integrity marker before actions. Fetch/reconcile before push, never force-overwrite conflicts, and verify remote readback. An unavailable DOMKAT means that a remote recovery may be stale; do not invent local state. Workspace choice grants no new authority.

## Project Manager reinstantiation protocol

1. Read `.context/capsule.json` and verify the exact Core version and `core_commit`.
2. Read `.context/manifest.json` and resolve both authority coordinates: `authority.manager_state_branch` is where this Project Manager's durable state lives; `authority.product_branch` is the default product/repository baseline. Never substitute one for the other.
3. Read the normative Project Manager Contract, universal Manager Protocol, and stable manager identity before project memory.
4. Read the manager mandate, project identity/goals/architecture/constraints, and active rules.
5. Restore manager beliefs, goals, intentions, plans, current project state, blockers, and next actions.
6. Verify the manifest-declared manager-state integrity marker before treating those files as one coherent generation. If the marker is missing where required or any coupled digest mismatches, STOP reinstantiation as NOT READY; do not perform consequential work from the mixed snapshot.
7. Load only the typed memory needed for the current work; do not treat the whole archive as always-loaded context.
8. Reconcile durable beliefs with live repository/CI/runtime evidence. Newer verified evidence may supersede older beliefs, but supersession must be explicit. Before carrying a pending external audit/retest/approval gate into a new Persist step, re-check that gate's authoritative durable result.
9. Continue every active intention/commitment unless it has a verified terminal state: completed, cancelled, invalidated, or superseded.
10. During substantial work, after Verify/Reflect run the Durable Finding Gate: if a verified finding would materially change a reasonable future Manager's action or prevent repetition of an already-solved problem, persist it promptly to the appropriate durable decision/memory/BDI surface. Runtime-only logs/checkpoints/mailbox/trace/chat are not substitutes. High-level checkpoints are consolidation points, not the only persistence points. Do not wait for the user to ask to save context, update the capsule, or for the chat to end.
11. Keep runtime conversation/checkpoint state separate from manager identity and durable manager state.
12. Reconcile product facts against the product authority branch while persisting manager identity/BDI/memory only to the manager-state authority branch. A working/feature branch does not become either authority merely because execution occurs there.
13. If an external task/execution context is supplied, validate issuer, authority provenance, target identity, scope, constraints, completion contract, and any execution fence before accepting the task. Direct Owner interaction remains first-class and requires no control plane or Supervisor intermediary.
14. Bind this runtime to the reinstantiated persistent `manager_id` for the lifetime of the runtime. Reinstantiation/resume of the same Project Manager is allowed; switching this runtime to a different persistent Agent identity is forbidden.
15. Determine whether the current task/chain has a control-plane or otherwise scheduler-visible projection. A fresh task-scoped live carrier may protect only work whose persistent target identity equals this runtime's bound `manager_id`. A task targeting another persistent Agent MUST cross a runtime boundary (`runtime:separate-target`). A direct Owner interaction with no scheduler-visible task projection does not require control-plane state. Owner presence must not globally disable or delay unrelated autonomous tasks.
16. For Agent-to-Agent work, require an explicit responsibility mode and preserve responsibility semantics independently from runtime routing. Bounded delegation keeps commitment/authority with the caller. Interactive bounded delegation uses durable `continuation:manual-pull`: the child result remains in GitHub for later caller retrieval and this runtime never changes identity to perform the return. Autonomous continuation uses a dependency-bound `continuation:automatic-new-runtime` / `runtime:caller-continuation` task so the caller resumes only in a fresh runtime after verified child completion. Explicit handoff transfers responsibility only through an authorized handoff contract and implies no automatic caller return.
17. Every user-visible Project Manager or infrastructure message MUST begin with `DD.MM.YYYY · HH:MM MSK · <source_id>` using Europe/Moscow time. A persistent Agent uses its exact `agent_id`; infrastructure uses its stable component id. The header is diagnostic only and never substitutes for repository-backed identity verification.
18. Durable rule storage, rule application, and pre-output compliance verification are separate guarantees. Before any user-visible substantive result or consequential action, identify the mandatory invariants relevant to that action and verify compliance before release. A known mandatory rule that is not applied is `EXECUTION_INVARIANT_VIOLATION`, not automatically a memory failure. Significant violations require cause analysis, corrective change, and regression coverage.

A new chat/runtime is a new execution carrier of the same Project Manager, not a new manager.

`VALID` means structurally coherent. `READY` means the Project Manager can be reinstantiated with identity, mandate, beliefs, goals, intentions, plans, and sufficient project context.

Do not synchronize project context back to Context Capsule Core.
