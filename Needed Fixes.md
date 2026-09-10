# IOTSnap HMI v1 Status Tracker

The authoritative execution order, acceptance criteria, and release gates are in [ROADMAP.md](ROADMAP.md). This file is intentionally short so status does not drift away from the plan.

## Completed foundation

- [x] First-run setup workflow and local roles.
- [x] OPC UA connection profiles, mapped live tag cache, and Comms live-value panel.
- [x] Typed OPC UA write pipeline and baseline stale/quality indicators.
- [x] Persisted alarm state and acknowledgement mechanics.
- [x] Screen/widget/binding schema, designer CRUD, publish action, and runtime published-payload API.
- [x] Numeric, command-button, and trend-chart widget contracts.
- [x] HMI package import/export and agent-assisted draft tooling.

## Current execution order

- [x] HMI-001: Finish and validate the in-progress runtime alarm, navigation, input, and chart slice.
- [x] HMI-002: Establish representative acceptance fixtures and release contracts. See [FactoryAcceptance.md](docs/FactoryAcceptance.md).
- [ ] HMI-010 through HMI-013: Close server-side control authorization, command confirmation, audit, and secret-protection gaps.
- [ ] Release Gate A: Prove the controlled runtime foundation.
- [ ] HMI-020 through HMI-022: Deliver durable alarm history, persisted trends, and resilient OPC UA behavior.
- [ ] HMI-030 through HMI-032: Complete the operator, designer, and package workflows.
- [ ] HMI-040 through HMI-042: Produce operational evidence and qualify the local release.
- [ ] Release Gate B: Approve the shippable local HMI v1 candidate.
