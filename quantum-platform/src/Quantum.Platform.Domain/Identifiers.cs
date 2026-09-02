using NOF.Domain;

namespace Quantum.Platform.Domain;

[NewableValueObject]
public readonly partial struct PlatformUserId : IValueObject<long>;

[NewableValueObject]
public readonly partial struct PluginListingId : IValueObject<long>;

[NewableValueObject]
public readonly partial struct PluginReleaseId : IValueObject<long>;

[NewableValueObject]
public readonly partial struct AuditEntryId : IValueObject<long>;
