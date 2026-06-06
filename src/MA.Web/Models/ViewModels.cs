using System.ComponentModel.DataAnnotations;

namespace MA.Web.Models;

public class LoginViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required, MaxLength(100)] public string UserName { get; set; } = "";
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), MinLength(8)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
}

public class ProductFormViewModel
{
    public int ProductId { get; set; }
    [Required, MaxLength(100)] public string SKU { get; set; } = "";
    [MaxLength(64)]            public string? Barcode { get; set; }
    [Required, MaxLength(500)] public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public string? Category { get; set; }
    [Range(0, 9999999)] public decimal MRP { get; set; }
    [Range(0, 9999999)] public decimal SellingPrice { get; set; }
    public int? WeightGrams { get; set; }
    public string? HSNCode { get; set; }
    public decimal? GSTPercent { get; set; }
    public string? ImageUrlsCsv { get; set; }
}

public class MarketplaceAccountViewModel
{
    public int AccountId { get; set; }
    [Required] public string Marketplace { get; set; } = "Flipkart";
    [Required, MaxLength(200)] public string DisplayName { get; set; } = "";
    [Required] public string UserName { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? SellerId { get; set; }
}
