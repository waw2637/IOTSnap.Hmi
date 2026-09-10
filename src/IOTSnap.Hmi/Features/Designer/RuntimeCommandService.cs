using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Runtime.OpcUa;
using Microsoft.EntityFrameworkCore;

namespace IOTSnap.Hmi.Features.Designer;

public sealed class RuntimeCommandService(IDbContextFactory<HmiDbContext> dbFactory, IOpcUaRuntime runtime)
{
    public async Task<RuntimeCommandResult> ExecuteAsync(
        RuntimeActor actor,
        RuntimeActionTarget target,
        string value,
        bool requiresConfirmation,
        string idempotencyKey,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var command = await db.OperatorCommands
            .FirstOrDefaultAsync(x => x.ActorUsername == actor.Username && x.IdempotencyKey == idempotencyKey, cancellationToken);

        if (command is not null && command.Status != "ConfirmationRequired")
        {
            return RuntimeCommandResult.From(command);
        }

        if (command is null)
        {
            command = new OperatorCommand
            {
                IdempotencyKey = idempotencyKey,
                ActorUsername = actor.Username,
                ActorRole = actor.Role,
                ScreenSlug = target.ScreenSlug,
                WidgetKey = target.WidgetKey,
                BindingRole = target.BindingRole,
                NodeId = target.NodeId,
                RequestedValue = value,
                RequiresConfirmation = requiresConfirmation,
                Status = requiresConfirmation ? "ConfirmationRequired" : "Requested"
            };
            command.ConfirmationExpiresUtc = requiresConfirmation ? DateTimeOffset.UtcNow.AddSeconds(30) : null;
            db.OperatorCommands.Add(command);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException)
            {
                command = await db.OperatorCommands.FirstAsync(x => x.ActorUsername == actor.Username && x.IdempotencyKey == idempotencyKey, cancellationToken);
            }
        }

        if (command.Status == "ConfirmationRequired" && command.ConfirmationExpiresUtc <= DateTimeOffset.UtcNow)
        {
            command.Status = "TimedOut";
            command.CompletedUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return RuntimeCommandResult.From(command);
        }

        if (command.RequiresConfirmation && !confirmed)
        {
            return RuntimeCommandResult.From(command);
        }

        command.AcceptedUtc = DateTimeOffset.UtcNow;
        var writeResult = await runtime.WriteTagAsync(command.NodeId, command.RequestedValue, cancellationToken);
        command.OpcUaResponse = writeResult.Message;
        if (writeResult.Succeeded)
        {
            var observedValue = (await runtime.GetTagSnapshotsAsync(cancellationToken))
                .FirstOrDefault(x => string.Equals(x.NodeId, command.NodeId, StringComparison.OrdinalIgnoreCase))?
                .ValueText;
            command.ObservedValue = observedValue;
            command.ObservedUtc = DateTimeOffset.UtcNow;
            command.Status = RuntimeCommandConfirmation.Matches(command.RequestedValue, observedValue)
                ? "Confirmed"
                : "Accepted";
        }
        else
        {
            command.Status = "Failed";
        }
        command.CompletedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return RuntimeCommandResult.From(command);
    }
}

public sealed record RuntimeCommandResult(string CommandId, string Status, string? Message, bool RequiresConfirmation)
{
    public static RuntimeCommandResult From(OperatorCommand command)
        => new(command.CommandId, command.Status, command.OpcUaResponse, command.RequiresConfirmation);
}
