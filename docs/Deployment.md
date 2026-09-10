# Local Deployment, Upgrade, Backup, and Restore

## Supported topology

IOTSnap HMI v1 is a single-site, single-instance ASP.NET Core deployment. Run it under one local service account. The account must have exclusive read/write access to the application's `data` directory.

Required persistent files:

- `data/iotsnap-hmi.db`: SQLite operational database.
- `data/keys`: ASP.NET Core data-protection keys, including protected OPC UA credentials.
- `data/opcua/pki`: Local OPC UA application, trusted peer, trusted issuer, and rejected certificate stores.

Back up and restore these paths together. Restoring the database without `data/keys` makes protected credentials unreadable.

## Install

1. Install the supported .NET runtime matching the application target framework.
2. Deploy the application files to a local directory owned by the HMI service account.
3. Create `data` under the application content root and restrict it to that account.
4. Optionally set `ConnectionStrings__Hmi` to a SQLite path. The default is `Data Source={DataDirectory}/iotsnap-hmi.db`.
5. Start the service. Startup creates `data/keys` and applies EF Core migrations automatically.
6. Browse to `/healthz`, complete first-run setup, and verify `/readyz` after configuring OPC UA.

Do not place the SQLite database or key directory on a shared writable network location.

## Upgrade

1. Stop the HMI service and confirm no process retains the database.
2. Create and verify a backup as described below.
3. Deploy the new application files without replacing `data`.
4. Start the service. EF Core applies pending migrations before serving requests.
5. Check `/healthz`, `/readyz`, the audit view, a published runtime screen, and historical trends.

If startup migration fails, stop the service, retain the failure logs, restore the verified backup, and restart the previous application version. Never hand-edit the SQLite schema.

## Backup

1. Prefer a maintenance window and stop the service to obtain a consistent filesystem copy.
2. Copy the complete `data` directory, including `iotsnap-hmi.db`, any SQLite sidecar files, `keys`, and `opcua/pki`.
3. Record the application version, backup time, and checksum of the copied files.
4. Store the backup where only authorized operational staff can read it. The key directory is sensitive material.

Take a pre-upgrade backup and a scheduled daily backup. Retain backups according to the site's operational policy and trend-retention needs.

## Restore

1. Stop the destination HMI service.
2. Move the destination `data` directory aside; do not merge files.
3. Restore the complete backup `data` directory with its original ownership restrictions.
4. Start the service and allow migrations to run.
5. Verify `/healthz`, then confirm users, published screens, audit records, alarms, and trends are visible. Reconfigure or validate OPC UA connectivity as needed.

If the database is corrupt or `data/keys` is missing, restore a complete verified backup. A replacement key directory cannot decrypt existing protected credentials; update the profile password through the Admin communications page after recovery if necessary.

## Importing OPC UA certificates

Admins can import trusted OPC UA server or issuer certificates directly from the Communications page. Upload `.cer`, `.crt`, `.der`, or `.pem` files into either the Trusted Application or Trusted Issuer store.

Imported certificates are written under `data/opcua/pki` and are used by both the runtime client and the discovery client.

## Recovery checks

- `GET /healthz` confirms process liveness.
- `GET /readyz` returns `503` for a configured but disconnected runtime, which is expected until OPC UA is healthy.
- Do not treat stale, bad-quality, or disconnected OPC UA values as current process data.
- Review the audit log after any restore or profile change.

## Disposable OPC UA qualification endpoint

Use `deploy/deploy-opc-plc.sh` to provision the disposable `opc-plc` test server on `cp1` for release qualification. The script SSHes to `cp1`, runs `microk8s kubectl diff/apply` against `deploy/opc-plc-test.yaml`, waits for the deployment to become ready, and prints the resulting pod, service, endpoint, and recent logs.

Use `deploy/teardown-opc-plc.sh` to remove the same disposable resources after qualification. It copies the manifest to `cp1`, deletes the `opc-plc` deployment and service from `workloads`, waits for termination, and prints the remaining matching resources as proof of cleanup.

Use `deploy/roundtrip-opc-plc.sh` to run repeated deploy/teardown verification cycles. This is the durability check for the disposable harness itself and should be used before treating the simulator workflow as a stable qualification dependency.

For the standalone harness description, upstream sample context, cluster assumptions, and future extraction boundary, see [deploy/README.md](/home/williamwatson/repo/IOTSnap.Hmi/deploy/README.md).

The in-cluster OPC UA endpoint is `opc.tcp://opc-plc.workloads.svc.cluster.local:50000`.
