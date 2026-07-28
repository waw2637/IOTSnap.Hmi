# IOTSnap.Hmi Roadmap

This document captures the product direction for turning the current prototype into a full-featured HMI platform.

## Near-term priorities

### 1. Operator-grade runtime features
- Alarm management with active/alarm history, acknowledgment, shelving, and severity
- Trend views with real-time updates and historical playback
- Basic navigation between screens and multi-screen layouts
- Faceplate/detail views for equipment and process objects
- Tag quality/status indicators for connection and data validity
- Touch-friendly numeric input popups with min/max validation and HMI-side scaling/offset formatting

### 2. Reliability and control
- Better OPC UA reconnect and recovery behavior
- Write-back confirmation, timeout handling, and command feedback
- Debounce and deadband controls for noisy tags
- Offline-safe behavior for transient connectivity issues

### 3. Designer productivity
- Reusable widget templates and style libraries
- Tag browser and drag-and-drop binding experience
- Screen versioning and change history
- Better import/export of larger projects and bundled assets

### 4. Security and governance
- Role-based access for screens, tags, and commands
- Audit trails for operator actions and writes
- Secure configuration and secret handling
- Production deployment controls and environment separation

### 5. Data and operational visibility
- Historical data storage and retrieval
- Event and action logs
- Recipes/setpoint management
- Reporting and export for trends and alarms

### 6. Production readiness
- Logging, diagnostics, and monitoring
- Performance tuning for large tag sets and many screens
- Automated regression testing for runtime and designer workflows
- Packaging and deployment tooling for real-world installs

## Recommended implementation order

1. Alarms and trends
2. Navigation and faceplates
3. Reliable tag writes and subscriptions
4. Audit/security controls
5. Historical data and reporting

## Current repo status

The prototype already includes:
- a designer experience
- a runtime view
- property-style bindings
- OPC UA connectivity concepts
- package import/export support

The next milestone is to move from a demo-ready prototype toward a production-like HMI platform by focusing on alarms, trends, navigation, and runtime robustness.
