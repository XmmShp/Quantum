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
        services.AddSingleton<IPluginPackageStore, PhysicalPluginPackageStore>();
        services.AddSingleton<IMobileAttachmentStore, PhysicalMobileAttachmentStore>();
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
