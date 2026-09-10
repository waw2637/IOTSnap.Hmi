using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace IOTSnap.Hmi.Features.Designer;

public sealed class RuntimeAuditService(IDbContextFactory<HmiDbContext> dbFactory)
{
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b(password|pwd|secret|token)\s*([:=])\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task RecordAsync(RuntimeActor actor, string actionType, string resource, string result, string? correlationId, string? detail, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.OperatorAuditEntries.Add(new OperatorAuditEntry
        {
            ActorUsername = actor.Username,
            ActorRole = actor.Role,
            ActionType = actionType,
            Resource = resource,
            Result = result,
            CorrelationId = correlationId,
            Detail = RedactAndBound(detail)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? RedactAndBound(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;

        var redacted = SecretAssignment.Replace(detail, "$1$2[REDACTED]");
        return redacted.Length <= 512 ? redacted : redacted[..512];
    }
}
