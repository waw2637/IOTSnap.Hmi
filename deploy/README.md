# Disposable OPC PLC Qualification Harness

This directory contains a disposable Kubernetes harness for bringing up the Microsoft `opc-plc` simulator on `cp1`, validating that the lifecycle is repeatable, and tearing it back down after qualification.

It is intentionally structured so it can become its own repository without pulling application code along with it.

## What this is

This harness wraps the Microsoft OPC PLC sample server as a reusable test endpoint for OPC UA client and HMI integration work.

- Upstream sample: <https://learn.microsoft.com/en-us/samples/azure-samples/iot-edge-opc-plc/azure-iot-sample-opc-ua-server/>
- Container image: `mcr.microsoft.com/iotedge/opc-plc`
- Current endpoint shape in this harness: `opc.tcp://192.168.1.26:50000` for workstation-based testing, with `opc.tcp://opc-plc.workloads.svc.cluster.local:50000` retained for in-cluster consumers

The upstream sample is intended for simulation and test usage, not production deployment. That matches our use here: a disposable qualification endpoint for local cluster-based integration testing.

## Repository contents

- `opc-plc-test.yaml`: Kubernetes workload manifest pinned to `cp1`.
- `opc-plc.env`: shared harness configuration intended to be consumable by both shell scripts and a future MCP path.
- `opc-plc-common.sh`: shared SSH, namespace, manifest, and remote-execution configuration.
- `deploy-opc-plc.sh`: copies the manifest to `cp1`, diffs it, applies it, waits for rollout, and prints pod/service/log evidence.
- `teardown-opc-plc.sh`: deletes the same resources and waits until the cluster reports that they are gone.
- `roundtrip-opc-plc.sh`: runs repeated deploy/teardown cycles to verify the harness itself is durable.
- `run-live-qualification.sh`: deploys the simulator and runs the live qualification test category against the network-reachable endpoint.
- `run-ui-qualification.sh`: deploys the simulator and runs the Playwright UI workflow category against the network-reachable endpoint.

## Cluster assumptions

This harness assumes the `r740-dev` development cluster described in the infrastructure notes:

- `cp1` is reachable over SSH with `microk8s` installed locally on that node.
- `workloads` is the namespace used for disposable test services.
- The caller has an SSH key that can log into `cp1`.
- The manifest is applied from the workstation by copying it to `cp1` and executing `microk8s kubectl` there.

Default values are set for this environment, but the scripts can be redirected with environment variables such as `CP1_HOST`, `CP1_USER`, `SSH_KEY`, `KUBE_NAMESPACE`, and `MANIFEST_PATH`.

The committed `opc-plc.env` file is the shared configuration surface. Today the shell scripts source it directly; the intended next step is for an MCP-based configuration tool to read and update the same values rather than inventing a separate settings path.

## Quick start

Provision the simulator:

```bash
./deploy/deploy-opc-plc.sh
```

Tear it down:

```bash
./deploy/teardown-opc-plc.sh
```

Validate the lifecycle with repeated create/destroy cycles:

```bash
CYCLES=2 ./deploy/roundtrip-opc-plc.sh
```

Run the live qualification test category:

```bash
./deploy/run-live-qualification.sh
```

Run the UI workflow qualification category:

```bash
./deploy/run-ui-qualification.sh
```

## What the scripts do

`deploy-opc-plc.sh`:

- Copies `opc-plc-test.yaml` to `cp1`.
- Prints current cluster state in `workloads`.
- Runs `microk8s kubectl diff`.
- Applies the manifest.
- Waits for rollout readiness.
- Prints deployment, pod, service, endpoints, and recent logs.

`teardown-opc-plc.sh`:

- Copies the same manifest to `cp1`.
- Prints existing matching resources.
- Deletes the manifest-managed resources.
- Waits until the matching deployment, pod, service, and endpoints disappear.
- Prints the remaining matching state as cleanup evidence.

`roundtrip-opc-plc.sh`:

- Runs deploy then teardown in sequence.
- Repeats the cycle `CYCLES` times.
- Exists non-zero if any deployment or cleanup step fails.

## Upstream sample behavior relevant to us

The Microsoft sample supports more than just a basic listener. The Learn page documents:

- `--pn=50000` for the OPC UA port.
- `--autoaccept` for automatic certificate acceptance in test scenarios.
- `--nodesfile` for custom writable node definitions.
- `--alm` and `--dalm=<file>` for alarm and deterministic alarm testing.
- Additional simulated data modes including fast/slow nodes, boiler models, heartbeats, and stacklight behavior.

That matters because this harness can evolve from a single generic endpoint into a reusable test kit with multiple fixture profiles.

## Current manifest intent

The current manifest keeps the footprint intentionally simple:

- One replica.
- Scheduled to `cp1`.
- Tolerates control-plane taints.
- Exposes OPC UA on port `50000`.
- Binds `hostPort: 50000` on `cp1` so workstations on the same network can use the simulator without an SSH tunnel.
- Uses readiness and liveness TCP probes.
- Publishes a ClusterIP service for in-cluster consumers.

This is enough for HMI integration and release-qualification work without turning the harness into a large platform project yet.

## Evidence collected so far

On Thursday, August 27, 2026, this harness was validated on `cp1` with:

- Successful deploy and rollout.
- Successful teardown and clean resource removal.
- Successful `CYCLES=2` round-trip verification ending with no matching `opc-plc` resources remaining in `workloads`.

## Extraction plan

This is a strong candidate for its own GitHub repository because it already has:

- A standalone Kubernetes manifest.
- Shell-based lifecycle tooling.
- A durability verifier.
- A clear infrastructure target and operating model.
- No runtime dependency on the HMI application.

If we split it out, the clean boundary is this entire `deploy/` directory. A future standalone repo should add:

- Version pinning for the image instead of `:latest`.
- Fixture files for nodes, alarms, and deterministic scenarios.
- Profile-specific manifests or script flags.
- A short changelog of verified cluster runs.
