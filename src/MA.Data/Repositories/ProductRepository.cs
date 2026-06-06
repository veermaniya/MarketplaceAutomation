using MA.Core.Dtos;
using MA.Core.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MA.Data.Repositories;

public interface IProductRepository
{
    Task<List<DuplicateConflict>> CheckDuplicatesAsync(string sku, string? barcode, int? excludeProductId = null);
    Task<Product?> GetWithImagesAsync(int productId);
    Task<int> AddAsync(Product product);
    Task UpdateAsync(Product product);
}

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;
    public ProductRepository(AppDbContext db) { _db = db; }

    public async Task<List<DuplicateConflict>> CheckDuplicatesAsync(string sku, string? barcode, int? excludeProductId = null)
    {
        // Call sp_Product_ValidateDuplicates. Returns 0..N rows.
        var conflicts = new List<DuplicateConflict>();

        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.sp_Product_ValidateDuplicates";
        cmd.CommandType = System.Data.CommandType.StoredProcedure;
        cmd.Parameters.Add(new SqlParameter("@ProductId", (object?)excludeProductId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SKU", sku));
        cmd.Parameters.Add(new SqlParameter("@Barcode", (object?)barcode ?? DBNull.Value));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            conflicts.Add(new DuplicateConflict
            {
                Field                = reader.GetString(0),
                ConflictingProductId = reader.GetInt32(1),
                ConflictingTitle     = reader.GetString(2),
                OwnerUserId          = reader.GetInt32(3)
            });
        }
        return conflicts;
    }

    public Task<Product?> GetWithImagesAsync(int productId) =>
        _db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.ProductId == productId);

    public async Task<int> AddAsync(Product product)
    {
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return product.ProductId;
    }

    public async Task UpdateAsync(Product product)
    {
        product.UpdatedOn = DateTime.UtcNow;
        _db.Products.Update(product);
        await _db.SaveChangesAsync();
    }
}
