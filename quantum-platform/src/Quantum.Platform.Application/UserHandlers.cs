using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application.Handlers;

public sealed class RegisterUser(
    IRepository<PlatformUser> users,
    IPlatformPasswordHasher passwordHasher,
    AuditWriter auditWriter,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    IDbContext dbContext) : QuantumPlatformService.RegisterUser
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
            var exists = await users.AsNoTracking().AnyAsync(
                candidate => candidate.Username == username || candidate.Email == email,
                cancellationToken);
            if (exists)
            {
                return Result.Fail("user_conflict", "The username or email address is already registered.");
            }

            var user = PlatformUser.Create(
                username,
                email,
                passwordHasher.Hash(request.Password),
                idGenerator: idGenerator,
                timeProvider: timeProvider);
            await users.AddAsync(user, cancellationToken);
            await auditWriter.WriteAsync(
                "user.registered",
                user.Id,
                $"User '{user.Username}' registered.",
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

public sealed class Login(
    IRepository<PlatformUser> users,
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
        var issuedToken = tokenIssuer.Issue(user);
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
