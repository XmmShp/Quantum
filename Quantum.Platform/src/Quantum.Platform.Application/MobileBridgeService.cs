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
    TimeProvider clock,
    IMobileAttachmentStore attachments) : MobileBridgeService.ExchangeMobileBridge
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
            if (request.Operation == "attachment/create" && result.GetProperty("ready").GetBoolean())
            {
                var file = MobileJson.Read<BridgeAttachment>(result.GetProperty("attachment"));
                try
                {
                    await attachments.CompleteAsync(user.Id.ToString(), file, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or BridgeException)
                {
                    mailbox.Attachments.Single(a => a.File.Id == file.Id).Ready = false;
                    result = MobileJson.Element(new { attachment = file, ready = false });
                }
            }
            if (request.Operation is "attachment/write" or "attachment/complete" or "attachment/read")
            {
                var file = MobileJson.Read<BridgeAttachment>(result);
                var state = mailbox.Attachments.Single(a => a.File.Id == file.Id);
                var accountKey = user.Id.ToString();
                if (request.Operation == "attachment/write" && !state.Ready)
                {
                    var data = request.Body.GetProperty("dataBase64").GetString() ?? throw new ArgumentException("Attachment data is required.");
                    if (data.Length > ((MobileAttachments.ChunkBytes + 2) / 3) * 4) throw new ArgumentException("Attachment chunk too large.");
                    await attachments.WriteAsync(accountKey, file, request.Body.GetProperty("offset").GetInt64(), Convert.FromBase64String(data), cancellationToken);
                }
                else if (request.Operation == "attachment/complete")
                {
                    await attachments.CompleteAsync(accountKey, file, cancellationToken);
                    state.Ready = true;
                }
                else if (request.Operation == "attachment/read")
                {
                    var data = await attachments.ReadAsync(accountKey, file, request.Body.GetProperty("offset").GetInt64(), cancellationToken);
                    result = MobileJson.Element(new { dataBase64 = Convert.ToBase64String(data) });
                }
            }
            account.Update(JsonSerializer.Serialize(mailbox, MobileJson.Options));
            if (isNew) await accounts.AddAsync(account, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success<JsonElement?>(result);
        }
        catch (IOException) { return Result.Fail("mobile_attachment_unavailable", "Attachment storage is temporarily unavailable."); }
        catch (BridgeException exception) { return Result.Fail(exception.Code, exception.Message); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Result.Fail("mobile_invalid_request", "Invalid or excessive mobile bridge payload.");
        }
    }
}
