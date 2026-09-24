using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;

namespace MinGo.MyBillBook.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PaymentPlatform> PaymentPlatforms => Set<PaymentPlatform>();
    public DbSet<FundAccount> FundAccounts => Set<FundAccount>();
    public DbSet<BillRawRecord> BillRawRecords => Set<BillRawRecord>();
    public DbSet<NormalizedTransaction> NormalizedTransactions => Set<NormalizedTransaction>();
    public DbSet<BillRecord> BillRecords => Set<BillRecord>();
    public DbSet<ClassificationResult> ClassificationResults => Set<ClassificationResult>();
    public DbSet<DuplicateCandidate> DuplicateCandidates => Set<DuplicateCandidate>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagRule> TagRules => Set<TagRule>();
    public DbSet<TransactionTag> TransactionTags => Set<TransactionTag>();
    public DbSet<CategoryTag> CategoryTags => Set<CategoryTag>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<BalanceSnapshot> BalanceSnapshots => Set<BalanceSnapshot>();
    public DbSet<ReconciliationIssue> ReconciliationIssues => Set<ReconciliationIssue>();
    public DbSet<BillCategory> BillCategories => Set<BillCategory>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantAlias> MerchantAliases => Set<MerchantAlias>();
    public DbSet<BillImportBatch> BillImportBatches => Set<BillImportBatch>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<PipelineStepRun> PipelineStepRuns => Set<PipelineStepRun>();
    public DbSet<PipelineJob> PipelineJobs => Set<PipelineJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaymentPlatform>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<FundAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Balance).HasConversion<decimal>();
            e.HasOne(x => x.Platform).WithMany(p => p.FundAccounts).HasForeignKey(x => x.PlatformId);
        });

        modelBuilder.Entity<BillRawRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RawPayload).IsRequired();
            e.Property(x => x.SourceTransactionId).HasMaxLength(200);
            e.Property(x => x.SourcePaymentTransactionId).HasMaxLength(200);
            e.HasIndex(x => x.SourceTransactionId);
            e.HasIndex(x => x.SourcePaymentTransactionId);
            e.HasOne(x => x.ImportBatch).WithMany(b => b.RawRecords).HasForeignKey(x => x.ImportBatchId);
            e.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId);
        });

        modelBuilder.Entity<NormalizedTransaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.RawDescription).HasMaxLength(500);
            e.Property(x => x.Counterparty).HasMaxLength(200);
            e.Property(x => x.ProductName).HasMaxLength(500);
            e.Property(x => x.Direction).HasMaxLength(20);
            e.Property(x => x.PaymentMethod).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.SourceTransactionId).HasMaxLength(200);
            e.Property(x => x.SourcePaymentTransactionId).HasMaxLength(200);
            e.HasOne(x => x.RawRecord).WithMany().HasForeignKey(x => x.RawRecordId);
            e.HasIndex(x => x.RawRecordId);
            e.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.Entity<BillRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Merchant).HasMaxLength(200);
            e.Property(x => x.Counterparty).HasMaxLength(200);
            e.Property(x => x.ProductName).HasMaxLength(500);
            e.Property(x => x.SourceTransactionId).HasMaxLength(200);
            e.HasOne(x => x.RawRecord).WithMany().HasForeignKey(x => x.RawRecordId);
            e.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId);
            e.HasOne(x => x.FundAccount).WithMany().HasForeignKey(x => x.FundAccountId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
            e.HasOne(x => x.MerchantRef).WithMany(m => m.BillRecords).HasForeignKey(x => x.MerchantId);
            e.HasIndex(x => x.TransactionDate);
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.MerchantId);
            e.HasIndex(x => x.SyncedToDuckDb);
            e.HasIndex(x => x.SourceTransactionId);
        });

        modelBuilder.Entity<ClassificationResult>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Value).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Transaction).WithMany(t => t.ClassificationResults)
                .HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TransactionId);
            e.HasIndex(x => new { x.TransactionId, x.Field });
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.TagType);
        });

        modelBuilder.Entity<TagRule>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ConditionValue).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Tag).WithMany(t => t.Rules).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TagId);
            e.HasIndex(x => x.Priority);
        });

        modelBuilder.Entity<TransactionTag>(e =>
        {
            e.HasKey(x => new { x.TransactionId, x.TagId });
            e.HasOne(x => x.Transaction).WithMany(t => t.Tags).HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany(t => t.TransactionTags).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TagId);
        });

        modelBuilder.Entity<CategoryTag>(e =>
        {
            e.HasKey(x => new { x.CategoryId, x.TagId });
            e.HasOne(x => x.Category).WithMany(c => c.CategoryTags).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany(t => t.CategoryTags).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TagId);
        });

        modelBuilder.Entity<DuplicateCandidate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.MatchReason).HasMaxLength(500);
            e.HasOne(x => x.LeftRecord).WithMany().HasForeignKey(x => x.LeftRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RightRecord).WithMany().HasForeignKey(x => x.RightRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.LeftRecordId);
        });

        modelBuilder.Entity<Transfer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.MatchedRecordIds).HasMaxLength(200);
            e.HasOne(x => x.FromAccount).WithMany().HasForeignKey(x => x.FromAccountId);
            e.HasOne(x => x.ToAccount).WithMany().HasForeignKey(x => x.ToAccountId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.Entity<BalanceSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BankBalance).HasConversion<decimal>();
            e.Property(x => x.CalculatedBalance).HasConversion<decimal>();
            e.Property(x => x.Difference).HasConversion<decimal>();
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AccountId, x.SnapshotDate });
        });

        modelBuilder.Entity<ReconciliationIssue>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Difference).HasConversion<decimal>();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Snapshot).WithMany(s => s.Issues).HasForeignKey(x => x.SnapshotId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.AccountId);
        });

        modelBuilder.Entity<BillCategory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId);
        });

        modelBuilder.Entity<CategoryRule>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.MatchPattern).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Category).WithMany(c => c.Rules).HasForeignKey(x => x.CategoryId);
            e.HasIndex(x => x.Priority);
        });

        modelBuilder.Entity<Merchant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CanonicalName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.CanonicalName).IsUnique();
            e.HasOne(x => x.DefaultCategory).WithMany().HasForeignKey(x => x.DefaultCategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MerchantAlias>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Pattern).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Merchant).WithMany(m => m.Aliases).HasForeignKey(x => x.MerchantId);
            e.HasIndex(x => x.MerchantId);
            e.HasIndex(x => x.Priority);
        });

        modelBuilder.Entity<BillImportBatch>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId);
        });

        modelBuilder.Entity<PipelineRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Version).HasMaxLength(20);
            e.HasOne(x => x.ImportBatch).WithMany().HasForeignKey(x => x.ImportBatchId);
            e.HasIndex(x => x.ImportBatchId);
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<PipelineStepRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.StepName).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.Run).WithMany(r => r.Steps).HasForeignKey(x => x.PipelineRunId);
            e.HasIndex(x => x.PipelineRunId);
        });

        modelBuilder.Entity<PipelineJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).IsRequired();
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => x.Status);
        });
    }
}
