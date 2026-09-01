using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Quantum.Plugin.Abstraction;

/// <summary>
/// Provides late-bound service resolution for plugin integrations that cannot reference the
/// service contract at compile time.
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// Gets a service by its exact CLR full type name (<see cref="Type.FullName"/>).
    /// </summary>
    /// <param name="services">The provider that owns the service lifetime.</param>
    /// <param name="serviceTypeName">
    /// The namespace-qualified CLR type name, without a <c>global::</c> prefix or assembly name.
    /// </param>
    /// <returns>The resolved service, or <see langword="null"/> when the type or registration is not found.</returns>
    /// <remarks>
    /// Resolution uses <paramref name="services"/> directly and does not cache either the type or
    /// the instance. Scoped services must therefore be requested from the appropriate scoped
    /// provider; their disposal remains owned by that scope.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="serviceTypeName"/> is empty, assembly-qualified, or starts with <c>global::</c>.
    /// </exception>
    /// <exception cref="AmbiguousMatchException">
    /// More than one loaded service type with the requested full name can be selected.
    /// </exception>
    public static object? GetService(this IServiceProvider services, string serviceTypeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateServiceTypeName(serviceTypeName);

        var candidates = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(serviceTypeName, throwOnError: false, ignoreCase: false))
            .Where(static type => type is not null)
            .Cast<Type>()
            .Distinct()
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var serviceType = SelectServiceType(services, serviceTypeName, candidates);
        return serviceType is null ? null : services.GetService(serviceType);
    }

    private static Type? SelectServiceType(
        IServiceProvider services,
        string serviceTypeName,
        IReadOnlyList<Type> candidates)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var serviceLookup = services as IServiceProviderIsService
            ?? services.GetService(typeof(IServiceProviderIsService)) as IServiceProviderIsService;
        if (serviceLookup is null)
        {
            throw CreateAmbiguousMatchException(serviceTypeName, candidates);
        }

        var registeredCandidates = candidates
            .Where(serviceLookup.IsService)
            .ToArray();

        return registeredCandidates.Length switch
        {
            0 => null,
            1 => registeredCandidates[0],
            _ => throw CreateAmbiguousMatchException(serviceTypeName, registeredCandidates)
        };
    }

    private static void ValidateServiceTypeName(string serviceTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceTypeName);

        if (!string.Equals(serviceTypeName, serviceTypeName.Trim(), StringComparison.Ordinal)
            || serviceTypeName.StartsWith("global::", StringComparison.Ordinal)
            || serviceTypeName.Contains(',', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The service type name must be an exact Type.FullName without whitespace, a global:: prefix, or an assembly name.",
                nameof(serviceTypeName));
        }
    }

    private static AmbiguousMatchException CreateAmbiguousMatchException(
        string serviceTypeName,
        IEnumerable<Type> candidates)
    {
        var assemblies = candidates
            .Select(static type => type.Assembly.GetName().Name ?? type.Assembly.FullName ?? "<unknown>")
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new AmbiguousMatchException(
            $"More than one loaded service type has the full name '{serviceTypeName}' and cannot be selected unambiguously: "
            + string.Join(", ", assemblies)
            + ". Use a shared contract type or remove the duplicate registration.");
    }
}
