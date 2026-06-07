using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MA.Core.Enums;

namespace MA.Core.Entities;

public class User
{
    public int UserId { get; set; }
    [Required, MaxLength(100)] public string UserName { get; set; } = "";
    [Required, MaxLength(256)] public string Email { get; set; } = "";
    [Required] public string PasswordHash { get; set; } = "";
    [Required] public string PasswordSalt { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginOn { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class Role
{
    public int RoleId { get; set; }
    [Required, MaxLength(50)] public string RoleName { get; set; } = "";
    public string? Description { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class UserRole
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public int RoleId { get; set; }
    public Role? Role { get; set; }
}

public class Product
{
    public int ProductId { get; set; }
    public int OwnerUserId { get; set; }
    [Required, MaxLength(100)] public string SKU { get; set; } = "";
    [MaxLength(64)]            public string? Barcode { get; set; }
    [Required, MaxLength(500)] public string Title { get; set; } = "";
    public string? Description { get; set; }
    [MaxLength(150)] public string? Brand { get; set; }
    [MaxLength(200)] public string? Category { get; set; }
    public decimal MRP { get; set; }
    public decimal SellingPrice { get; set; }
    public int? WeightGrams { get; set; }
    [MaxLength(20)]  public string? HSNCode { get; set; }
    public decimal? GSTPercent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOn { get; set; }

    public User? Owner { get; set; }
    public ICollection<ProductImage>         Images   { get; set; } = new List<ProductImage>();
    public ICollection<MarketplaceMapping>   Mappings { get; set; } = new List<MarketplaceMapping>();
}

public class ProductImage
{
    public int ImageId { get; set; }
    public int ProductId { get; set; }
    [Required, MaxLength(1000)] public string ImageUrl { get; set; } = "";
    [MaxLength(1000)] public string? LocalPath { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public Product? Product { get; set; }
}

public class MarketplaceAccount
{
    public int AccountId { get; set; }
    public int UserId { get; set; }
    [Required, MaxLength(20)] public string Marketplace { get; set; } = "";
    [Required, MaxLength(200)] public string DisplayName { get; set; } = "";
    [Required] public string EncryptedUserName { get; set; } = "";
    [Required] public string EncryptedPassword { get; set; } = "";
    public string? EncryptedApiKey    { get; set; }
    public string? EncryptedApiSecret { get; set; }
    [MaxLength(100)] public string? SellerId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedOn { get; set; }
    public User? User { get; set; }
}

public class MarketplaceMapping
{
    public int MappingId { get; set; }
    public int ProductId { get; set; }
    public int AccountId { get; set; }
    [Required, MaxLength(20)] public string Marketplace { get; set; } = "";
    [MaxLength(100)] public string? ExternalListingId { get; set; }
    [MaxLength(100)] public string? ExternalSKU { get; set; }
    [MaxLength(500)] public string? CategoryPath { get; set; }
    [Required, MaxLength(30)] public string Status { get; set; } = nameof(MappingStatus.Pending);
    public DateTime? LastSyncedOn { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public Product?            Product   { get; set; }
    public MarketplaceAccount? Account   { get; set; }
    public Inventory?          Inventory { get; set; }
}

public class Inventory
{
    public int InventoryId { get; set; }
    public int MappingId { get; set; }
    public int AvailableQty { get; set; }
    public int ReservedQty { get; set; }
    public decimal Price { get; set; }
    public DateTime? LastPushedOn { get; set; }
    public DateTime? LastPullOn   { get; set; }
    public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
    public MarketplaceMapping? Mapping { get; set; }
}

public class Order
{
    public int OrderId { get; set; }
    public int AccountId { get; set; }
    [Required, MaxLength(20)] public string Marketplace { get; set; } = "";
    [Required, MaxLength(100)] public string MarketplaceOrderId { get; set; } = "";
    public int? ProductId { get; set; }
    public int? MappingId { get; set; }
    [Required, MaxLength(50)] public string OrderStatus { get; set; } = "";
    public int OrderQty { get; set; }
    public decimal OrderAmount { get; set; }
    [MaxLength(200)] public string? BuyerName { get; set; }
    [MaxLength(100)] public string? BuyerCity { get; set; }
    public DateTime OrderedOn { get; set; }
    public DateTime FetchedOn { get; set; } = DateTime.UtcNow;
    public string? RawPayload { get; set; }
}

public class AutomationLog
{
    [Key]
    public long LogId { get; set; }
    public int? UserId { get; set; }
    public int? AccountId { get; set; }
    public int? MappingId { get; set; }
    [MaxLength(20)]  public string? Marketplace { get; set; }
    [Required, MaxLength(100)] public string Action { get; set; } = "";
    [Required, MaxLength(20)]  public string Status { get; set; } = "";
    public string? Message { get; set; }
    public int? DurationMs { get; set; }
    public DateTime OccurredOn { get; set; } = DateTime.UtcNow;
}

public class RetryQueueItem
{
    public long QueueId { get; set; }
    [Required, MaxLength(50)] public string JobType { get; set; } = "";
    [Required] public string Payload { get; set; } = "";
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime NextAttemptOn { get; set; } = DateTime.UtcNow;
    [Required, MaxLength(20)] public string Status { get; set; } = nameof(RetryStatus.Pending);
    public string? LastError { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOn { get; set; }
}
