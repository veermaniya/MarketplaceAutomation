namespace MA.Core.Dtos;

public class ProductImportRow
{
    public string SKU { get; set; } = "";
    public string? Barcode { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public string? Category { get; set; }
    public decimal MRP { get; set; }
    public decimal SellingPrice { get; set; }
    public int? WeightGrams { get; set; }
    public string? HSNCode { get; set; }
    public decimal? GSTPercent { get; set; }
    public List<string> ImageUrls { get; set; } = new();
}

public class ProductImportResult
{
    public int RowNumber { get; set; }
    public string SKU { get; set; } = "";
    public bool Success { get; set; }
    public int? ProductId { get; set; }
    public string? Error { get; set; }
}

public class DuplicateConflict
{
    public string Field { get; set; } = "";   // 'SKU' or 'Barcode'
    public int ConflictingProductId { get; set; }
    public string ConflictingTitle { get; set; } = "";
    public int OwnerUserId { get; set; }
}

public class ListingJobPayload
{
    public int MappingId { get; set; }
    public int ProductId { get; set; }
    public int AccountId { get; set; }
    public string Marketplace { get; set; } = "";
}

public class InventoryPushJobPayload
{
    public int MappingId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
