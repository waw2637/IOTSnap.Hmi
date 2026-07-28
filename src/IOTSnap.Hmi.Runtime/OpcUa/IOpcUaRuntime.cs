namespace IOTSnap.Hmi.Runtime.OpcUa;

public interface IOpcUaRuntime
{
    Task<OpcUaRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpcUaTagSnapshot>> GetTagSnapshotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpcUaAlarmSnapshot>> GetAlarmsAsync(bool includeCleared = false, CancellationToken cancellationToken = default);
    Task<OpcUaTagWriteResult> WriteTagAsync(string nodeId, string valueText, CancellationToken cancellationToken = default);
    Task<OpcUaAlarmCommandResult> AcknowledgeAlarmAsync(string nodeId, string acknowledgedBy, CancellationToken cancellationToken = default);
    Task RefreshNowAsync(CancellationToken cancellationToken = default);
}
