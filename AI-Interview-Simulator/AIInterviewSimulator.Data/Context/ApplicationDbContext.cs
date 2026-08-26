using AIInterviewSimulator.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIInterviewSimulator.Data.Context;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<InterviewSession> InterviewSessions { get; set; }
    public DbSet<UserAnswer> UserAnswers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InterviewSession>()
            .HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId);

        modelBuilder.Entity<InterviewSession>()
            .HasIndex(s => s.UserId)
            .IsUnique();

        modelBuilder.Entity<UserAnswer>()
            .HasOne(a => a.InterviewSession)
            .WithMany(s => s.Answers)
            .HasForeignKey(a => a.InterviewSessionId);
    }
}
