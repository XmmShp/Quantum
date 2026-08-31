using NOF.Domain;

namespace Quantum.ExtensionMarket.Domain;

[NewableValueObject]
public readonly partial struct MarketUserId : IValueObject<long>;

[NewableValueObject]
public readonly partial struct PluginListingId : IValueObject<long>;

[NewableValueObject]
public readonly partial struct PluginReleaseId : IValueObject<long>;

[NewableValueObject]
public readonly partial struct AuditEntryId : IValueObject<long>;
