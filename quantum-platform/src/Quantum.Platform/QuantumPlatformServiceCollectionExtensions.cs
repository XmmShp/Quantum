using Quantum.Platform.Application;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace Quantum.Platform;

public static class QuantumPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddQuantumPlatformServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PluginStorageOptions>(
            configuration.GetSection(PluginStorageOptions.SectionName));
        services.AddOptions<PlatformEmailOptions>()
            .Bind(configuration.GetSection(PlatformEmailOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => !options.Enabled
                    || (!string.IsNullOrWhiteSpace(options.SmtpHost)
                        && MailAddress.TryCreate(options.FromAddress, out _)
                        && (string.IsNullOrWhiteSpace(options.Username)
                            || !string.IsNullOrWhiteSpace(options.Password))),
                "QuantumPlatform:Email requires SmtpHost, a valid FromAddress, and a password when Username is set.")
            .ValidateOnStart();
        services.AddSingleton<IPlatformPasswordHasher, Pbkdf2PlatformPasswordHasher>();
        services.AddSingleton<IPluginPackageStore, PhysicalPluginPackageStore>();
        services.AddSingleton<IPlatformEmailSender, MailKitPlatformEmailSender>();
        return services;
    }
}
