using Microsoft.Extensions.Logging;
using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application.Handlers;

public sealed class RequestPasswordResetEmailCode(
    IRepository<PlatformUser> users,
    IRepository<PasswordResetVerification> verifications,
    IPlatformPasswordHasher passwordHasher,
    IEmailVerificationCodeGenerator codeGenerator,
    IRegistrationLock registrationLock,
    IPlatformEmailSender emailSender,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    IDbContext dbContext,
    ILogger<RequestPasswordResetEmailCode> logger) : QuantumPlatformService.RequestPasswordResetEmailCode
{
    public override async Task<Result> HandleAsync(
        RequestPasswordResetEmailCodeRequest request, Context context, CancellationToken cancellationToken)
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

        await using var lease = await registrationLock.AcquireAsync($"password-reset-email:{email}", cancellationToken);
        var user = await users.AsNoTracking().Where(candidate => candidate.Email == email)
            .SingleOrDefaultAsync(cancellationToken);
        // Keep the public response identical for unknown addresses and throttled requests.
        if (user is null)
        {
            return Result.Success();
        }

        var verification = await verifications.Where(candidate => candidate.Email == email)
            .SingleOrDefaultAsync(cancellationToken);
        if (verification is not null && !verification.CanResend(timeProvider))
        {
            return Result.Success();
        }

        var code = codeGenerator.Generate();
        var codeHash = passwordHasher.Hash(code);
        // Deliver before mutating tracked entities, so a failed send cannot persist a new code.
        try
        {
            await emailSender.SendAsync(
                email,
                "Quantum Platform password reset code",
                $"Your Quantum Platform password reset code is {code}. It expires in 10 minutes. If you did not request this, ignore this email.",
                $"<p>Your Quantum Platform password reset code is <strong>{code}</strong>.</p><p>It expires in 10 minutes. If you did not request this, ignore this email.</p>",
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not send a password reset verification email.");
            return Result.Success();
        }

        if (verification is not null && verification.UserId != user.Id)
        {
            verifications.Remove(verification);
            verification = null;
        }
        if (verification is null)
        {
            verification = PasswordResetVerification.Create(email, user.Id, codeHash, idGenerator, timeProvider);
            await verifications.AddAsync(verification, cancellationToken);
        }
        else
        {
            verification.ReplaceCode(codeHash, timeProvider);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class ResetPassword(
    IRepository<PlatformUser> users,
    IRepository<PasswordResetVerification> verifications,
    IPlatformPasswordHasher passwordHasher,
    IRegistrationLock registrationLock,
    AuditWriter auditWriter,
    TimeProvider timeProvider,
    IDbContext dbContext) : QuantumPlatformService.ResetPassword
{
    public override async Task<Result> HandleAsync(
        ResetPasswordRequest request, Context context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length is < 12 or > 1024)
        {
            return Result.Fail("invalid_password", "Password must contain 12 to 1024 characters.");
        }

        string email;
        try
        {
            email = PlatformUser.NormalizeEmail(request.Email);
        }
        catch (ArgumentException)
        {
            return InvalidCode();
        }

        await using var lease = await registrationLock.AcquireAsync($"password-reset-email:{email}", cancellationToken);
        var verification = await verifications.Where(candidate => candidate.Email == email)
            .SingleOrDefaultAsync(cancellationToken);
        var user = await users.Where(candidate => candidate.Email == email).SingleOrDefaultAsync(cancellationToken);
        if (verification is null || !verification.CanVerify(timeProvider) || user is null || verification.UserId != user.Id)
        {
            return InvalidCode();
        }

        string? code = null;
        try
        {
            code = PasswordResetVerification.NormalizeCode(request.VerificationCode);
        }
        catch (ArgumentException)
        {
            // Malformed guesses also consume an attempt.
        }
        if (code is null || !passwordHasher.Verify(verification.CodeHash, code))
        {
            verification.RecordFailedAttempt();
            await dbContext.SaveChangesAsync(cancellationToken);
            return InvalidCode();
        }

        user.ResetPassword(passwordHasher.Hash(request.NewPassword));
        verifications.Remove(verification);
        await auditWriter.WriteAsync("user.password_reset", user.Id,
            $"Password for user '{user.Username}' was reset via email.", null, null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result InvalidCode()
        => Result.Fail("invalid_verification_code", "The verification code is incorrect or expired. Request a new code.");
}
