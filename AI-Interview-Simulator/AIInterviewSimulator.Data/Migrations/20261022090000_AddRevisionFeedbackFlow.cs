using System;
using AIInterviewSimulator.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIInterviewSimulator.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261022090000_AddRevisionFeedbackFlow")]
public partial class AddRevisionFeedbackFlow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("FlowVersion", "InterviewSessions", type: "INTEGER", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<string>("RevisedFeedbackText", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("FeedbackReadyAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("RevisedFeedbackReadyAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("FeedbackDisplayAcknowledgedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("RevisedFeedbackDisplayAcknowledgedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("FeedbackBrowserDisplayedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("RevisedFeedbackBrowserDisplayedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("QuestionBrowserDisplayedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("OriginalBrowserSubmittedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("RevisionEditorBrowserDisplayedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("RevisionBrowserSubmittedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("StageCompletedAt", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("FeedbackLeaseUntil", "UserAnswers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<double>("OriginalWritingMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("RevisionWritingMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("DecisionMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("RevisedFeedbackReadingMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("StageElapsedMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("FeedbackWaitMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("RevisedFeedbackWaitMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("FeedbackGenerationMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<double>("RevisedFeedbackGenerationMilliseconds", "UserAnswers", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<int>("FeedbackGenerationAttempts", "UserAnswers", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>("RevisedFeedbackGenerationAttempts", "UserAnswers", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>("FeedbackLeaseRound", "UserAnswers", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<Guid>("FeedbackLeaseId", "UserAnswers", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("RevisedFeedbackText", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackReadyAt", "UserAnswers");
        migrationBuilder.DropColumn("RevisedFeedbackReadyAt", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackDisplayAcknowledgedAt", "UserAnswers");
        migrationBuilder.DropColumn("RevisedFeedbackDisplayAcknowledgedAt", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackBrowserDisplayedAt", "UserAnswers");
        migrationBuilder.DropColumn("RevisedFeedbackBrowserDisplayedAt", "UserAnswers");
        migrationBuilder.DropColumn("QuestionBrowserDisplayedAt", "UserAnswers");
        migrationBuilder.DropColumn("OriginalBrowserSubmittedAt", "UserAnswers");
        migrationBuilder.DropColumn("RevisionEditorBrowserDisplayedAt", "UserAnswers");
        migrationBuilder.DropColumn("RevisionBrowserSubmittedAt", "UserAnswers");
        migrationBuilder.DropColumn("StageCompletedAt", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackLeaseUntil", "UserAnswers");
        migrationBuilder.DropColumn("OriginalWritingMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("RevisionWritingMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("DecisionMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("RevisedFeedbackReadingMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("StageElapsedMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackWaitMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("RevisedFeedbackWaitMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackGenerationMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("RevisedFeedbackGenerationMilliseconds", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackGenerationAttempts", "UserAnswers");
        migrationBuilder.DropColumn("RevisedFeedbackGenerationAttempts", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackLeaseRound", "UserAnswers");
        migrationBuilder.DropColumn("FeedbackLeaseId", "UserAnswers");
        migrationBuilder.DropColumn("FlowVersion", "InterviewSessions");
    }
}
