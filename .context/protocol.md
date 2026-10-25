# Context Capsule protocol

## Purpose

Context Capsule v2 stores the durable state required to reinstate the same Project Manager across independent chats, models, or agent runtimes.

The runtime is replaceable. The manager identity, mandate, durable beliefs, goals, intentions, plans, and typed memory are repository-local and persistent.

## Durable layers

- `project/` — objective project identity, goals, architecture, and constraints.
- `manager/` — stable manager identity, mandate, beliefs, goals, intentions, plans, and the Core-managed Manager Protocol.
- `memory/` — typed semantic, episodic, and procedural memory.
- `rules/` and `decisions/` — binding rules and durable accepted/rejected choices.
- `current/` — compact verified project state, blockers, and next work.
- `handoffs/latest.md` — emergency/convenience summary only; it is not the identity or primary continuity mechanism.
- `dialogues/` and `history/` — deeper evidence and historical context.

## Authority coordinates

Context Capsule v2 separates two branch roles:

- `authority.manager_state_branch` — the branch that owns the durable Project Manager identity, BDI state, memory, and working views;
- `authority.product_branch` — the default branch whose live repository state represents the product baseline the manager reconciles against.

The compatibility field `authoritative_branch` is an alias for `authority.manager_state_branch`; it never means product authority in v2. `discovery_branch` is only a bootstrap/discovery coordinate.

A feature/runtime branch does not become manager-state or product authority merely because work executes there.

## Runtime boundary

Conversation history, pending tool calls, chain-of-thought, transient execution state, leases, heartbeats, and runtime checkpoints are not Project Manager identity. A runtime may persist its own checkpoint separately, but Context Capsule does not require or impersonate that checkpoint.

## Belief provenance

Durable manager beliefs must retain provenance. Distinguish owner directives, verified repository/CI/runtime evidence, trusted external evidence, and manager inference. Never silently promote untrusted content or a specialist agent's output into authoritative belief.

When reconciling evidence, classify the relation to the existing proposition before changing state:

- **confirm** — same semantic claim remains true; newer evidence may strengthen provenance without requiring a rewrite;
- **supersede** — adjudicated evidence changes the value/truth of the proposition; preserve the old record as superseded and update active state;
- **conflict** — incompatible evidence cannot yet be adjudicated; preserve both sides explicitly.

Freshness alone is not supersession. A newer timestamp, commit, CI run, or repeated successful check is only confirming evidence if the semantic state is unchanged.

## VALID vs READY

`VALID` means the v2 capsule is structurally coherent, repository-confined, internally resolvable, and includes the manager model.

`READY` additionally means the Project Manager can be reinstantiated: mandate, beliefs with provenance, manager goals, intentions, plans, core project semantics, current state, and next work are substantive.

## Working views and consistency

The authoritative durable manager state is the manager BDI state plus verified live evidence. `current/state.md`, `current/blockers.md`, `current/next.md`, and `handoffs/latest.md` are compact working views, not independent sources of truth.

A working view is stale only when its semantic projection is false or materially misleading. A later confirming event does not make it stale merely because the evidence is newer. If a working view semantically conflicts with manager beliefs, intentions/plans, or verified repository/CI/runtime evidence, preserve the conflict during reconciliation, use the higher-authority evidence when adjudication is possible, then repair all affected views in the same semantic write-back.

When one verified event changes several working views, update the affected views together. Do not leave a resolved blocker or obsolete next action behind after state/intentions have advanced.

## Semantic self-maintenance

Persist durable decisions, requirements, rules, architecture changes, root causes, rejected approaches, milestones, beliefs, commitments, lessons, learned procedures, and priority changes.

Periodically consolidate memory after major milestones or when the working set becomes noisy. Consolidation must preserve provenance and important superseded history.

Do not copy routine runtime churn into `.context/`.

## Repository mutation

Canonical GitHub lifecycle changes are planned and validated as a complete snapshot and published from an expected parent with a non-forced ref update.

## Privacy

Never persist credentials, secret values, cookies, private keys, hidden chain-of-thought, or unnecessary sensitive personal information.
