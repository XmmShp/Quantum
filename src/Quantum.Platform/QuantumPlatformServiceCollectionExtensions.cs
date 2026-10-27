using Quantum.Platform.Application;
using System.Net.Mail;
using Quantum.Platform.SkillDistribution;

namespace Quantum.Platform;

public static class QuantumPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddQuantumPlatformServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<QuantumPublishSkillPackageCache>();
        services.Configure<PluginStorageOptions>(
            configuration.GetSection(PluginStorageOptions.SectionName));
        services.AddOptions<AgentFnAutomatedReviewOptions>()
            .Bind(configuration.GetSection(AgentFnAutomatedReviewOptions.SectionName))
            .PostConfigure(options => options.DevelopmentAutoApprove = environment.IsDevelopment())
            .ValidateDataAnnotations()
            .Validate(static options => options.IsValid(), "QuantumPlatform:AutomatedReview configuration is invalid.")
            .ValidateOnStart();
        services.AddOptions<PlatformEmailOptions>()
            .Bind(configuration.GetSection(PlatformEmailOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => environment.IsDevelopment()
                    || (options.Enabled
                        && !string.IsNullOrWhiteSpace(options.SmtpHost)
                        && MailAddress.TryCreate(options.FromAddress, out _)
                        && (string.IsNullOrWhiteSpace(options.Username)
                            || !string.IsNullOrWhiteSpace(options.Password))),
                "Non-development environments require enabled email, SmtpHost, a valid FromAddress, and a password when Username is set.")
            .ValidateOnStart();
        services.AddSingleton<IPlatformPasswordHasher, Pbkdf2PlatformPasswordHasher>();
        if (string.Equals(configuration["QuantumPlatform:Storage:Provider"], "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<S3StorageOptions>(configuration.GetSection("QuantumPlatform:Storage:S3"));
            services.AddOptions<S3StorageOptions>()
                .Bind(configuration.GetSection("QuantumPlatform:Storage:S3"))
                .Validate(options => Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var url)
                    && url.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(url.UserInfo)
                    && !string.IsNullOrWhiteSpace(options.BucketName)
                    && !string.IsNullOrWhiteSpace(options.AccessKeyId)
                    && !string.IsNullOrWhiteSpace(options.SecretAccessKey), "S3 configuration is incomplete.")
                .ValidateOnStart();
            services.AddSingleton<IQuantumObjectStore, S3ObjectStore>();
            services.AddSingleton<IPluginPackageStore, S3PluginPackageStore>();
            services.AddSingleton<IMobileAttachmentStore, S3MobileAttachmentStore>();
            services.AddHostedService<S3AttachmentCleanup>();
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<IPluginPackageStore, PhysicalPluginPackageStore>();
            services.AddSingleton<IMobileAttachmentStore, PhysicalMobileAttachmentStore>();
        }
        else
        {
            throw new InvalidOperationException("Production Quantum requires S3 storage.");
        }
        services.AddSingleton<IEmailVerificationCodeGenerator, CryptographicEmailVerificationCodeGenerator>();
        services.AddSingleton<IRegistrationLock, PostgreSqlRegistrationLock>();
        if (environment.IsDevelopment())
        {
            services.AddSingleton<IPlatformEmailSender, DevelopmentLoggingPlatformEmailSender>();
        }
        else
        {
            services.AddSingleton<IPlatformEmailSender, MailKitPlatformEmailSender>();
        }
        if (environment.IsDevelopment())
        {
            services.AddSingleton<IPluginReleaseAutomatedReviewer, DevelopmentAutoApprovePluginReleaseReviewer>();
        }
        else
        {
            services.AddSingleton<IPluginReleaseAutomatedReviewer, AgentFnPluginReleaseAutomatedReviewer>();
        }
        services.AddHostedService<AutomatedReleaseReviewWorker>();
        return services;
    }
}
