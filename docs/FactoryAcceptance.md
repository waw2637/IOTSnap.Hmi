# Representative Factory Acceptance Fixture

This fixture defines the stable v1 demonstration process used by automated and manual acceptance checks. It is intentionally local and does not require a production PLC.

## Process Contract

| Resource | Node ID | Type | Expected behavior |
| --- | --- | --- | --- |
| Process value | `ns=3;s=FastUInt1` | UInt32, read-only | Numeric display and trend source. |
| Line start | `ns=3;s=Plant.Line.Start` | Boolean, writable | Operator/Admin command requiring confirmation. |
| Main screen | `main` | Published HMI screen | Contains a process-value widget and a start-command widget. |

## Role Matrix

| Action | Viewer | Operator | Admin |
| --- | --- | --- | --- |
| Read published screens and alarms | Allowed | Allowed | Allowed |
| Acknowledge an authorized alarm | Denied | Allowed | Allowed |
| Issue the line-start command | Denied | Allowed after confirmation | Allowed after confirmation |
| Change HMI or OPC UA configuration | Denied | Denied | Allowed |

## Factory Script

1. Start the HMI with a clean local database and complete first-run setup.
2. Configure the representative OPC UA endpoint and confirm both mappings have healthy snapshots.
3. Sign in as Viewer: open `main`, verify the process value is visible, and verify direct write and acknowledge requests are denied.
4. Sign in as Operator: open `main`, issue a confirmed line-start command, and verify its requested, accepted, and final result are shown.
5. Raise a configured alarm, acknowledge it as Operator, and verify the actor and timestamp survive an application restart.
6. Restart the HMI, reopen `main`, and verify the published screen, alarm state, command audit, and trend history remain available.
7. Disconnect the OPC UA server, verify stale/degraded presentation and write rejection, then reconnect and verify a single healthy subscription set resumes.

## Expected API Failure Shape

Runtime authorization failures use standard HTTP status codes: `401` for no authenticated actor, `403` for an authenticated actor without binding access, `404` for an unpublished or unknown target, and `400` for invalid command input. Error bodies must not include credentials, certificate material, or client-supplied actor identities.

## Credential Storage

OPC UA passwords are write-only and protected using local ASP.NET Core data-protection keys in `data/keys`. Backups must include this directory with the SQLite database; it is sensitive operational material and must remain readable only by the local HMI service account.
