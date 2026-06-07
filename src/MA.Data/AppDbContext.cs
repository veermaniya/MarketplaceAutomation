using MA.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MA.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User>                 Users                 => Set<User>();
    public DbSet<Role>                 Roles                 => Set<Role>();
    public DbSet<UserRole>             UserRoles             => Set<UserRole>();
    public DbSet<Product>              Products              => Set<Product>();
    public DbSet<ProductImage>         ProductImages         => Set<ProductImage>();
    public DbSet<MarketplaceAccount>   MarketplaceAccounts   => Set<MarketplaceAccount>();
    public DbSet<MarketplaceMapping>   MarketplaceMappings   => Set<MarketplaceMapping>();
    public DbSet<Inventory>            Inventory             => Set<Inventory>();
    public DbSet<Order>                Orders                => Set<Order>();
    public DbSet<AutomationLog>        AutomationLogs        => Set<AutomationLog>();
    public DbSet<RetryQueueItem>       RetryQueue            => Set<RetryQueueItem>();

    //protected override void OnModelCreating(ModelBuilder b)
    //{
    //    // ---- Users / Roles ----
    //    b.Entity<User>().HasIndex(x => x.Email).IsUnique();

    //    b.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
    //    b.Entity<UserRole>().HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId);
    //    b.Entity<UserRole>().HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId);

    //    // ---- Products ----
    //    b.Entity<Product>(p =>
    //    {
    //        p.Property(x => x.MRP).HasColumnType("decimal(18,2)");
    //        p.Property(x => x.SellingPrice).HasColumnType("decimal(18,2)");
    //        p.Property(x => x.GSTPercent).HasColumnType("decimal(5,2)");
    //        p.HasIndex(x => x.SKU).IsUnique();
    //        p.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
    //        p.HasIndex(x => x.OwnerUserId);
    //        p.HasIndex(x => x.Category);
    //        p.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    //    });

    //    b.Entity<ProductImage>(pi =>
    //    {
    //        pi.HasOne(x => x.Product).WithMany(p => p.Images).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    //        pi.HasIndex(x => x.ProductId);
    //    });

    //    // ---- Marketplace accounts/mappings ----
    //    b.Entity<MarketplaceAccount>(a =>
    //    {
    //        a.HasKey(x => x.AccountId);
    //        a.HasIndex(x => new { x.UserId, x.Marketplace });
    //        a.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    //    });

    //    b.Entity<MarketplaceMapping>(m =>
    //    {
    //        m.HasKey(x => x.MappingId);
    //        m.HasIndex(x => new { x.ProductId, x.AccountId, x.Marketplace }).IsUnique();
    //        m.HasIndex(x => new { x.Marketplace, x.ExternalListingId })
    //         .IsUnique()
    //         .HasFilter("[ExternalListingId] IS NOT NULL");
    //        m.HasIndex(x => x.Status);
    //        m.HasOne(x => x.Product).WithMany(p => p.Mappings).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    //        m.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    //    });

    //    b.Entity<Inventory>(i =>
    //    {
    //        i.HasKey(x => x.InventoryId);
    //        i.Property(x => x.Price).HasColumnType("decimal(18,2)");
    //        i.HasIndex(x => x.MappingId).IsUnique();
    //        i.HasOne(x => x.Mapping).WithOne(m => m.Inventory).HasForeignKey<Inventory>(x => x.MappingId).OnDelete(DeleteBehavior.Cascade);
    //    });

    //    // ---- Orders ----
    //    b.Entity<Order>(o =>
    //    {
    //        o.Property(x => x.OrderAmount).HasColumnType("decimal(18,2)");
    //        o.HasIndex(x => new { x.Marketplace, x.MarketplaceOrderId }).IsUnique();
    //        o.HasIndex(x => x.OrderedOn).IsDescending();
    //    });

    //    // ---- Logs / Retry queue ----
    //    b.Entity<AutomationLog>(l =>
    //    {
    //        l.HasIndex(x => x.OccurredOn).IsDescending();
    //        l.HasIndex(x => x.Status);
    //    });

    //    b.Entity<RetryQueueItem>(r =>
    //    {
    //        r.HasIndex(x => new { x.Status, x.NextAttemptOn })
    //         .HasFilter("[Status] = 'Pending'");
    //    });

    //    base.OnModelCreating(b);
    //}
    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---- Users / Roles ----
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();

        b.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        b.Entity<UserRole>().HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId);
        b.Entity<UserRole>().HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId);

        // ---- Products ----
        b.Entity<Product>(p =>
        {
            p.HasKey(x => x.ProductId);
            p.Property(x => x.MRP).HasColumnType("decimal(18,2)");
            p.Property(x => x.SellingPrice).HasColumnType("decimal(18,2)");
            p.Property(x => x.GSTPercent).HasColumnType("decimal(5,2)");
            p.HasIndex(x => x.SKU).IsUnique();
            p.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            p.HasIndex(x => x.OwnerUserId);
            p.HasIndex(x => x.Category);
            p.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ProductImage>(pi =>
        {
            pi.HasKey(x => x.ImageId);
            pi.HasOne(x => x.Product).WithMany(p => p.Images).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            pi.HasIndex(x => x.ProductId);
        });

        // ---- Marketplace accounts/mappings ----
        b.Entity<MarketplaceAccount>(a =>
        {
            a.HasKey(x => x.AccountId);
            a.HasIndex(x => new { x.UserId, x.Marketplace });
            a.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MarketplaceMapping>(m =>
        {
            m.HasKey(x => x.MappingId);
            m.HasIndex(x => new { x.ProductId, x.AccountId, x.Marketplace }).IsUnique();
            m.HasIndex(x => new { x.Marketplace, x.ExternalListingId })
             .IsUnique()
             .HasFilter("[ExternalListingId] IS NOT NULL");
            m.HasIndex(x => x.Status);
            m.HasOne(x => x.Product).WithMany(p => p.Mappings).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            m.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Inventory>(i =>
        {
            i.HasKey(x => x.InventoryId);
            i.Property(x => x.Price).HasColumnType("decimal(18,2)");
            i.HasIndex(x => x.MappingId).IsUnique();
            i.HasOne(x => x.Mapping).WithOne(m => m.Inventory).HasForeignKey<Inventory>(x => x.MappingId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Orders ----
        b.Entity<Order>(o =>
        {
            o.HasKey(x => x.OrderId);
            o.Property(x => x.OrderAmount).HasColumnType("decimal(18,2)");
            o.HasIndex(x => new { x.Marketplace, x.MarketplaceOrderId }).IsUnique();
            o.HasIndex(x => x.OrderedOn).IsDescending();
        });

        // ---- Logs / Retry queue ----
        b.Entity<AutomationLog>(l =>
        {
            l.HasKey(x => x.LogId);
            l.HasIndex(x => x.OccurredOn).IsDescending();
            l.HasIndex(x => x.Status);
        });

        b.Entity<RetryQueueItem>(r =>
        {
            r.HasKey(x => x.QueueId);
            r.HasIndex(x => new { x.Status, x.NextAttemptOn })
             .HasFilter("[Status] = 'Pending'");
        });

        base.OnModelCreating(b);
    }
}
