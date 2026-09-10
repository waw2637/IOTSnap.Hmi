# UI Qualification Notes

## Run Date

- Local run date: August 27, 2026
- Live OPC endpoint used: `opc.tcp://192.168.1.26:50000`
- Browser path used by Playwright: `/usr/bin/google-chrome`

## Browser Workflow Coverage

- First-run setup using manual OPC endpoint entry, browse-based tag import, finish, and landing on the home screen.
- First-run setup using subnet scan, endpoint selection, browse-based tag import, finish, and landing on the home screen.

## Blocking Issues Found And Fixed

- The first-run redirect middleware blocked `/_blazor`, which meant `/setup` rendered but never became interactive for anonymous first-run users.
- The setup wizard relied on default input binding timing, which made step transitions fragile when users filled fields and immediately advanced.
- Manual OPC entry did not align its draft security settings to the real server endpoint before browsing tags, which caused browse/import to fail against the live PLC.
- The app emitted a `404` for `/favicon.ico`, which polluted browser diagnostics during UI runs.

## Non-Blocking Observations

- Immediately after a server reboot or fresh redeploy, `192.168.1.26:50000` may briefly refuse connections before the OPC PLC becomes reachable from the workstation. Retrying after rollout readiness resolved this during the August 27, 2026 run.
- The live PLC namespace can expose repeated display names such as `FastUInt1`, so UI automation should target stable node IDs when it needs a specific row.
- The manual flow may persist the discovered endpoint variant rather than the literal endpoint string typed by the user. The workflow still completes successfully as long as a single profile and imported mappings are saved.

## Commands

```bash
IOTSNAP_LIVE_OPC_ENDPOINT=opc.tcp://192.168.1.26:50000 \
  dotnet test tests/IOTSnap.Hmi.Tests/IOTSnap.Hmi.Tests.csproj --filter Category=UiPlaywright
```

```bash
./deploy/run-ui-qualification.sh
```
