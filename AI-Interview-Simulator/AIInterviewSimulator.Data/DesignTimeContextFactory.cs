using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using AIInterviewSimulator.Data.Context;

namespace AIInterviewSimulator.Data;

// Tooling never loads application settings/secrets or opens the real study database implicitly.
public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "interview-design-only.db")}").Options);
}
