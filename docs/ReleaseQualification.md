# Release Qualification Checklist

## Automated evidence

- [x] Clean `dotnet restore IOTSnap.Hmi.slnx` succeeds.
- [x] Clean `dotnet build IOTSnap.Hmi.slnx --no-restore` succeeds.
- [x] `dotnet test IOTSnap.Hmi.slnx --no-restore --no-build` succeeds.
- [x] Runtime authorization, command lifecycle, audit, credential protection, alarms, trends, package validation, health, and migration-backed publication history have focused automated coverage.
- [x] `git diff --check` succeeds.

## Manual OPC UA acceptance

Run [FactoryAcceptance.md](FactoryAcceptance.md) against a representative OPC UA server before release. Record the target endpoint, server version, tester, date, and result for each item:

For the in-cluster test endpoint, run `deploy/deploy-opc-plc.sh`. The script copies `deploy/opc-plc-test.yaml` to `cp1`, performs `microk8s kubectl diff`, applies the manifest in `workloads`, waits for rollout, and prints pod/service evidence. When the qualification run is complete, run `deploy/teardown-opc-plc.sh` to delete the disposable deployment and confirm the `opc-plc` resources are gone. Use `deploy/roundtrip-opc-plc.sh` when validating the harness itself across repeated deploy/teardown cycles. The HMI endpoint is `opc.tcp://opc-plc.workloads.svc.cluster.local:50000`; the deployment is pinned to `cp1` and tolerates control-plane taints.

- [ ] Viewer can read but cannot write or acknowledge.
- [ ] Operator performs a confirmation-required command and audit correlation is durable.
- [ ] Alarm raise, acknowledge, clear, and re-raise are visible after restart.
- [ ] Persisted trend range remains available after restart.
- [ ] Disconnect shows degraded/stale state and rejects writes; reconnect recovers one subscription set.
- [ ] Export/import preserves a draft screen and requires a validated publish.
- [ ] Backup is restored into a clean installation and read-only verification succeeds.

## Browser acceptance

Exercise the supported desktop browser at the deployment resolution:

- [ ] Viewer, Operator, and Admin navigation flows.
- [ ] Keyboard navigation and touch-sized control operation.
- [ ] Failed write recovery, stale data, alarm acknowledgement, navigation, and trend playback.

## Release decision

Do not release with an unresolved critical/high authentication, control-safety, or data-loss defect. Attach the completed checklist and factory record to the release artifact.
