# Context Capsule entrypoint

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
