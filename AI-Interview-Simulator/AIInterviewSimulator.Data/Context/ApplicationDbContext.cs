using AIInterviewSimulator.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIInterviewSimulator.Data.Context;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<InterviewSession> InterviewSessions { get; set; }
    public DbSet<UserAnswer> UserAnswers { get; set; }

    public DbSet<ParticipantCredential> ParticipantCredentials { get; set; }
    public DbSet<ConsentRecord> ConsentRecords { get; set; }
    public DbSet<QuestionnaireDefinition> QuestionnaireDefinitions { get; set; }
    public DbSet<QuestionnaireAdministration> QuestionnaireAdministrations { get; set; }
    public DbSet<QuestionnaireResponse> QuestionnaireResponses { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InterviewSession>().Property(s => s.FlowVersion).HasDefaultValue(1);

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

        modelBuilder.Entity<UserAnswer>()
            .HasIndex(a => a.SubmissionId)
            .IsUnique();
        modelBuilder.Entity<ParticipantCredential>().HasIndex(c => c.TokenHash).IsUnique();
        modelBuilder.Entity<ParticipantCredential>().HasIndex(c => c.UserId).IsUnique();
        modelBuilder.Entity<ParticipantCredential>().HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ConsentRecord>().HasIndex(c => c.UserId).IsUnique();
        modelBuilder.Entity<ConsentRecord>().HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<QuestionnaireDefinition>().HasIndex(q => new { q.Code, q.Version, q.Phase }).IsUnique();
        modelBuilder.Entity<QuestionnaireAdministration>().HasIndex(q => new { q.InterviewSessionId, q.Code, q.Version, q.Phase }).IsUnique();
        modelBuilder.Entity<QuestionnaireAdministration>().HasOne<InterviewSession>().WithMany().HasForeignKey(q => q.InterviewSessionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<QuestionnaireAdministration>().HasOne<User>().WithMany().HasForeignKey(q => q.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<QuestionnaireResponse>().HasIndex(q => new { q.AdministrationId, q.ItemCode }).IsUnique();
        modelBuilder.Entity<QuestionnaireResponse>().HasOne<QuestionnaireAdministration>().WithMany().HasForeignKey(q => q.AdministrationId).OnDelete(DeleteBehavior.Restrict);

    }
}
