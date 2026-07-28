# IOTSnap HMI v1 TODO

## Completed Foundation

- [x] First-run setup workflow replaces hardcoded defaults.
- [x] Live subscribed tag cache and Comms live-value panel.
- [x] Safe tag write pipeline with role gating and typed conversion.
- [x] Alarm and stale-data indicators from live quality + freshness.
- [x] Persisted alarm lifecycle (active/acknowledged/cleared) and acknowledge actions.

## v1 Delivery Plan

### 1) Designer Backend Contract (In Progress)

- [ ] Add screen schema with draft/published state.
- [ ] Add widget schema for core types: numeric, command button, trend chart.
- [ ] Add widget binding schema (read and write intent, role requirement).
- [ ] Add server-side validation for layout and bindings.

### 2) Designer/Runtime APIs

- [ ] Add CRUD endpoints for screens and widgets.
- [ ] Add publish endpoint to freeze runtime payload.
- [ ] Add runtime endpoint to fetch published screen payload by slug.

### 3) Initial Runtime Renderer

- [ ] Add renderer page that loads published payload and renders core widget types.
- [ ] Bind numeric widgets to live OPC UA tag snapshots.
- [ ] Bind command button widgets to safe write pipeline.

### 4) Trend/Historian Backend (Minimal)

- [ ] Add time-series sample persistence for configured trend tags.
- [ ] Add trend query endpoint with range and downsampling.

### 5) Operator Audit Trail

- [ ] Persist writes, alarm acknowledges, and publish actions.
- [ ] Add table endpoint for audit records.

## v1 Exit Criteria

- [ ] One published runtime screen can be rendered from DB payload.
- [ ] Numeric read + command write + trend chart work end-to-end.
- [ ] Alarm lifecycle and acknowledgement visible in runtime UX.
- [ ] Audit records capture operator writes and acknowledgements.