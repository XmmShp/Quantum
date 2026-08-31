using NOF.Domain;

namespace Quantum.ExtensionMarket.Domain;

public static class DependencyDefaults
{
    extension(IIdGenerator? idGenerator)
    {
        public IIdGenerator OrDefault() => idGenerator ?? IdGenerator.Current;
    }

    extension(TimeProvider? timeProvider)
    {
        public TimeProvider OrDefault() => timeProvider ?? TimeProvider.System;
    }
}
