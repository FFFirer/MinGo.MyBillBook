using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;

namespace MinGo.MyBillBook.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PaymentPlatform> PaymentPlatforms => Set<PaymentPlatform>();
    public DbSet<FundAccount> FundAccounts => Set<FundAccount>();
    public DbSet<BillRawRecord> BillRawRecords => Set<BillRawRecord>();
    public DbSet<BillRecord> BillRecords => Set<BillRecord>();
    public DbSet<BillCategory> BillCategories => Set<BillCategory>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<BillImportBatch> BillImportBatches => Set<BillImportBatch>();

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
            e.Property(x => x.RawData).IsRequired();
            e.Property(x => x.TransactionId).HasMaxLength(200);
            e.HasIndex(x => x.TransactionId);
            e.HasOne(x => x.ImportBatch).WithMany(b => b.RawRecords).HasForeignKey(x => x.ImportBatchId);
            e.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId);
        });

        modelBuilder.Entity<BillRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasConversion<decimal>();
            e.Property(x => x.Merchant).HasMaxLength(200);
            e.Property(x => x.Counterparty).HasMaxLength(200);
            e.Property(x => x.ProductName).HasMaxLength(500);
            e.HasOne(x => x.RawRecord).WithMany().HasForeignKey(x => x.RawRecordId);
            e.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId);
            e.HasOne(x => x.FundAccount).WithMany().HasForeignKey(x => x.FundAccountId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
            e.HasIndex(x => x.TransactionDate);
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.SyncedToDuckDb);
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

        modelBuilder.Entity<BillImportBatch>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId);
        });
    }
}
