using System.Text.Json;
using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.Mobile.Internal;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

public partial class MobileBridgeService : RpcServer<IMobileBridgeService>;

public sealed class ExchangeMobileBridge(
    PlatformCallerResolver caller,
    IRepository<MobileBridgeAccount> accounts,
    IRegistrationLock accountLock,
    IDbContext db,
    TimeProvider clock) : MobileBridgeService.ExchangeMobileBridge
{
    public override async Task<Result<JsonElement?>> HandleAsync(BridgeRequest request, Context context, CancellationToken cancellationToken)
    {
        var resolution = await caller.RequireAsync(PlatformUserRole.None, cancellationToken);
        if (resolution.User is not { } user) return Result.Fail(resolution.ErrorCode!, resolution.ErrorMessage!);
        await using var lease = await accountLock.AcquireAsync($"mobile-account:{user.Id}", cancellationToken);
        var account = await accounts.Where(a => a.Id == user.Id).SingleOrDefaultAsync(cancellationToken);
        var isNew = account is null;
        account ??= MobileBridgeAccount.Create(user.Id);
        try
        {
            var mailbox = JsonSerializer.Deserialize<MobileBridgeMailbox>(account.StateJson, MobileJson.Options)!;
            var result = mailbox.Exchange(request, clock.GetUtcNow());
            account.Update(JsonSerializer.Serialize(mailbox, MobileJson.Options));
            if (isNew) await accounts.AddAsync(account, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success<JsonElement?>(result);
        }
        catch (BridgeException exception) { return Result.Fail(exception.Code, exception.Message); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Result.Fail("mobile_invalid_request", "Invalid or excessive mobile bridge payload.");
        }
    }
}
