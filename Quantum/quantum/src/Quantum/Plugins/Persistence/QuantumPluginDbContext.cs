using Microsoft.EntityFrameworkCore;
using NOF.Infrastructure.EntityFrameworkCore;

namespace Quantum.Plugins.Persistence;

internal sealed class QuantumPluginDbContext(DbContextOptions<QuantumPluginDbContext> options)
    : NOFDbContext(options);
