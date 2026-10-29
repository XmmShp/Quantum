using Microsoft.Extensions.Logging;
using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application.Handlers;

public sealed class RequestRegistrationEmailCode(
    IRepository<PlatformUser> users,
    IRepository<RegistrationEmailVerification> verifications,
    IPlatformPasswordHasher passwordHasher,
    IEmailVerificationCodeGenerator codeGenerator,
    IRegistrationLock registrationLock,
    IPlatformEmailSender emailSender,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    IDbContext dbContext,
    ILogger<RequestRegistrationEmailCode> logger) : QuantumPlatformService.RequestRegistrationEmailCode
{
    public override async Task<Result> HandleAsync(
        RequestRegistrationEmailCodeRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        string email;
        try
        {
            email = PlatformUser.NormalizeEmail(request.Email);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_email", exception.Message);
        }

        await using var emailLease = await registrationLock.AcquireAsync($"registration-email:{email}", cancellationToken);
        if (await users.AsNoTracking().AnyAsync(candidate => candidate.Email == email, cancellationToken))
        {
            return Result.Fail("user_conflict", "The email address is already registered.");
        }

        var verification = await verifications
            .Where(candidate => candidate.Email == email)
            .SingleOrDefaultAsync(cancellationToken);
        if (verification is not null && !verification.CanResend(timeProvider))
        {
            return Result.Fail("verification_throttled", "Wait one minute before requesting another verification code.");
        }

        var code = codeGenerator.Generate();
        var codeHash = passwordHasher.Hash(code);
        if (verification is null)
        {
            verification = RegistrationEmailVerification.Create(email, codeHash, idGenerator, timeProvider);
            await verifications.AddAsync(verification, cancellationToken);
        }
        else
        {
            verification.ReplaceCode(codeHash, timeProvider);
        }

        try
        {
            await emailSender.SendAsync(
                email,
                "Quantum Platform registration code",
                $"Your Quantum Platform registration code is {code}. It expires in 10 minutes.",
                $"<p>Your Quantum Platform registration code is <strong>{code}</strong>.</p><p>It expires in 10 minutes.</p>",
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not send a registration verification email.");
            return Result.Fail("email_delivery_failed", "The verification email could not be delivered. Try again later.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class RegisterUser(
    IRepository<PlatformUser> users,
    IRepository<RegistrationEmailVerification> verifications,
    IPlatformPasswordHasher passwordHasher,
    AuditWriter auditWriter,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    IDbContext dbContext,
    IRegistrationLock registrationLock,
    IPlatformEmailSender emailSender,
    ILogger<RegisterUser> logger) : QuantumPlatformService.RegisterUser
{
    public override async Task<Result<UserSummary>> HandleAsync(
        RegisterUserRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length is < 12 or > 1024)
        {
            return Result.Fail("invalid_password", "Password must contain 12 to 1024 characters.");
        }

        try
        {
            var username = PlatformUser.NormalizeUsername(request.Username);
            var email = PlatformUser.NormalizeEmail(request.Email);
            var verificationCode = RegistrationEmailVerification.NormalizeCode(request.VerificationCode);
            await using var emailLease = await registrationLock.AcquireAsync($"registration-email:{email}", cancellationToken);
            await using var registrationLease = await registrationLock.AcquireAsync("registration-first-user", cancellationToken);
            var exists = await users.AsNoTracking().AnyAsync(
                candidate => candidate.Username == username || candidate.Email == email,
                cancellationToken);
            if (exists)
            {
                return Result.Fail("user_conflict", "The username or email address is already registered.");
            }

            var verification = await verifications
                .Where(candidate => candidate.Email == email)
                .SingleOrDefaultAsync(cancellationToken);
            if (verification is null || !verification.CanVerify(timeProvider))
            {
                return Result.Fail("verification_expired", "Request a new email verification code.");
            }

            if (!passwordHasher.Verify(verification.CodeHash, verificationCode))
            {
                verification.RecordFailedAttempt();
                await dbContext.SaveChangesAsync(cancellationToken);
                return Result.Fail("invalid_verification_code", "The email verification code is incorrect.");
            }

            var isFirstUser = !await users.AsNoTracking().AnyAsync(cancellationToken);
            var user = PlatformUser.Create(
                username,
                email,
                passwordHasher.Hash(request.Password),
                PlatformUser.RegistrationRoles(isFirstUser),
                idGenerator: idGenerator,
                timeProvider: timeProvider);
            await users.AddAsync(user, cancellationToken);
            verifications.Remove(verification);
            await auditWriter.WriteAsync(
                "user.registered",
                user.Id,
                isFirstUser
                    ? $"Initial administrator '{user.Username}' registered."
                    : $"User '{user.Username}' registered.",
                null,
                null,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            try
            {
                await emailSender.SendAsync(
                    user.Email,
                    "Welcome to Quantum Platform",
                    $"Hello {user.Username}, your Quantum developer account is ready.",
                    $"<p>Hello <strong>{System.Net.WebUtility.HtmlEncode(user.Username)}</strong>,</p><p>Your Quantum developer account is ready.</p>",
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not send the developer welcome email to user {UserId}.", user.Id);
            }

            return user.ToSummary();
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_user", exception.Message);
        }
    }
}

public sealed class Login(
    IRepository<PlatformUser> users,
    IRepository<PlatformLoginSession> sessions,
    IRefreshTokenCodec refreshTokens,
    IPlatformPasswordHasher passwordHasher,
    IPlatformTokenIssuer tokenIssuer,
    AuditWriter auditWriter,
    TimeProvider timeProvider,
    IDbContext dbContext) : QuantumPlatformService.Login
{
    public override async Task<Result<LoginResponse>> HandleAsync(
        LoginRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        string email;
        try
        {
            email = PlatformUser.NormalizeEmail(request.Email);
        }
        catch (ArgumentException)
        {
            return Result.Fail("invalid_credentials", "The email address or password is incorrect.");
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Result.Fail("invalid_credentials", "The email address or password is incorrect.");
        }

        var user = await users
            .Where(candidate => candidate.Email == email)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            return Result.Fail("invalid_credentials", "The email address or password is incorrect.");
        }

        user.RecordLogin(timeProvider);
        var sessionId = Guid.NewGuid();
        var refreshToken = refreshTokens.Create(sessionId);
        var session = PlatformLoginSession.Create(user, sessionId, refreshToken, timeProvider.GetUtcNow().UtcDateTime);
        await sessions.AddAsync(session, cancellationToken);
        var issuedToken = tokenIssuer.Issue(user, sessionId);
        await auditWriter.WriteAsync(
            "user.logged_in",
            user.Id,
            $"User '{user.Username}' logged in.",
            null,
            null,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new LoginResponse
        {
            RefreshToken = refreshToken,
            RefreshExpiresAtUtc = session.ExpiresAtUtc,
            AccessToken = issuedToken.AccessToken,
            ExpiresAtUtc = issuedToken.ExpiresAtUtc,
            User = user.ToSummary()
        };
    }
}

public sealed class GetCurrentUser(PlatformCallerResolver callerResolver) : QuantumPlatformService.GetCurrentUser
{
    public override async Task<Result<UserSummary>> HandleAsync(
        EmptyRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.User, cancellationToken);
        return caller.User is { } user
            ? user.ToSummary()
            : Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
    }
}

public sealed class GetUser(
    IRepository<PlatformUser> users,
    PlatformCallerResolver callerResolver) : QuantumPlatformService.GetUser
{
    public override async Task<Result<UserSummary>> HandleAsync(
        GetUserRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.User, cancellationToken);
        if (caller.User is not { } currentUser)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        if (!HandlerSupport.TryParseUserId(request.UserId, out var userId))
        {
            return Result.Fail("invalid_user_id", "UserId must be a positive 64-bit integer string.");
        }

        if (currentUser.Id != userId && !currentUser.HasAnyRole(PlatformUserRole.Admin))
        {
            return Result.Fail("forbidden", "Only an administrator can view another user's profile.");
        }

        var user = await users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .SingleOrDefaultAsync(cancellationToken);
        return user is null
            ? Result.Fail("user_not_found", "The registered user was not found.")
            : user.ToSummary();
    }
}

public sealed class UpdateCurrentUser(
    IRepository<PlatformUser> users,
    PlatformCallerResolver callerResolver,
    AuditWriter auditWriter,
    IDbContext dbContext) : QuantumPlatformService.UpdateCurrentUser
{
    public override async Task<Result<UserSummary>> HandleAsync(
        UpdateCurrentUserRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.User, cancellationToken);
        if (caller.User is not { } user)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        try
        {
            var username = PlatformUser.NormalizeUsername(request.Username);
            var email = PlatformUser.NormalizeEmail(request.Email);
            var conflict = await users.AsNoTracking().AnyAsync(
                candidate => candidate.Id != user.Id &&
                    (candidate.Username == username || candidate.Email == email),
                cancellationToken);
            if (conflict)
            {
                return Result.Fail("user_conflict", "The username or email address is already registered.");
            }

            user.UpdateProfile(username, email);
            await auditWriter.WriteAsync(
                "user.updated",
                user.Id,
                $"User '{user.Username}' updated their profile.",
                null,
                null,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return user.ToSummary();
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_user", exception.Message);
        }
    }
}

public sealed class UpdateUser(
    IRepository<PlatformUser> users,
    PlatformCallerResolver callerResolver,
    AuditWriter auditWriter,
    IDbContext dbContext) : QuantumPlatformService.UpdateUser
{
    public override async Task<Result<UserSummary>> HandleAsync(
        UpdateUserRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.User, cancellationToken);
        if (caller.User is not { } currentUser)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        if (!HandlerSupport.TryParseUserId(request.UserId, out var userId))
        {
            return Result.Fail("invalid_user_id", "UserId must be a positive 64-bit integer string.");
        }

        if (currentUser.Id != userId && !currentUser.HasAnyRole(PlatformUserRole.Admin))
        {
            return Result.Fail("forbidden", "Only an administrator can update another user's profile.");
        }

        var user = await users.Where(candidate => candidate.Id == userId)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return Result.Fail("user_not_found", "The registered user was not found.");
        }

        try
        {
            var username = PlatformUser.NormalizeUsername(request.Username);
            var email = PlatformUser.NormalizeEmail(request.Email);
            var conflict = await users.AsNoTracking().AnyAsync(
                candidate => candidate.Id != user.Id &&
                    (candidate.Username == username || candidate.Email == email),
                cancellationToken);
            if (conflict)
            {
                return Result.Fail("user_conflict", "The username or email address is already registered.");
            }

            user.UpdateProfile(username, email);
            await auditWriter.WriteAsync(
                "user.updated",
                currentUser.Id,
                $"Profile for user '{user.Username}' was updated.",
                null,
                null,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return user.ToSummary();
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_user", exception.Message);
        }
    }
}

public sealed class ListUsers(
    IRepository<PlatformUser> users,
    PlatformCallerResolver callerResolver) : QuantumPlatformService.ListUsers
{
    public override async Task<Result<UserSummary[]>> HandleAsync(
        EmptyRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.Admin, cancellationToken);
        if (caller.User is null)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        var values = await users.AsNoTracking()
            .OrderBy(static user => user.Username)
            .ToArrayAsync(cancellationToken);
        return values.Select(static user => user.ToSummary()).ToArray();
    }
}

public sealed class SetUserRoles(
    IRepository<PlatformUser> users,
    PlatformCallerResolver callerResolver,
    AuditWriter auditWriter,
    IDbContext dbContext) : QuantumPlatformService.SetUserRoles
{
    public override async Task<Result<UserSummary>> HandleAsync(
        SetUserRolesRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.Admin, cancellationToken);
        if (caller.User is not { } administrator)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        if (!HandlerSupport.TryParseUserId(request.UserId, out var userId))
        {
            return Result.Fail("invalid_user_id", "UserId must be a positive 64-bit integer string.");
        }

        var user = await users.Where(candidate => candidate.Id == userId)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return Result.Fail("user_not_found", "The registered user was not found.");
        }

        try
        {
            user.SetRoles((PlatformUserRole)(int)request.Roles);
            if (user.Id == administrator.Id && !user.HasAnyRole(PlatformUserRole.Admin))
            {
                return Result.Fail("cannot_remove_current_admin", "An administrator cannot remove their own admin role.");
            }

            await auditWriter.WriteAsync(
                "user.roles_changed",
                administrator.Id,
                $"Roles for user '{user.Username}' changed to '{user.Roles}'.",
                null,
                null,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return user.ToSummary();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Result.Fail("invalid_user_roles", exception.Message);
        }
    }
}

public sealed class DeleteUser(
    IRepository<PlatformUser> users,
    IRepository<PluginListing> listings,
    PlatformCallerResolver callerResolver,
    AuditWriter auditWriter,
    IDbContext dbContext) : QuantumPlatformService.DeleteUser
{
    public override async Task<Result> HandleAsync(
        DeleteUserRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.Admin, cancellationToken);
        if (caller.User is not { } administrator)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        if (!HandlerSupport.TryParseUserId(request.UserId, out var userId))
        {
            return Result.Fail("invalid_user_id", "UserId must be a positive 64-bit integer string.");
        }

        if (administrator.Id == userId)
        {
            return Result.Fail("cannot_delete_current_user", "An administrator cannot delete their own account.");
        }

        var user = await users.Where(candidate => candidate.Id == userId)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return Result.Fail("user_not_found", "The registered user was not found.");
        }

        if (await listings.AsNoTracking().AnyAsync(candidate => candidate.AuthorUserId == userId, cancellationToken))
        {
            return Result.Fail("user_owns_plugins", "A user that owns plugin listings cannot be deleted.");
        }

        users.Remove(user);
        await auditWriter.WriteAsync(
            "user.deleted",
            administrator.Id,
            $"User '{user.Username}' was deleted.",
            null,
            null,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
