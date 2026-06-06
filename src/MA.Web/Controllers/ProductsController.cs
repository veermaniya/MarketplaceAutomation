using System.Globalization;
using System.Security.Claims;
using CsvHelper;
using CsvHelper.Configuration;
using MA.Core.Dtos;
using MA.Core.Entities;
using MA.Data;
using MA.Data.Repositories;
using MA.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MA.Web.Controllers;

[Authorize]
public class ProductsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IProductRepository _repo;
    private readonly ILogger<ProductsController> _log;

    public ProductsController(AppDbContext db, IProductRepository repo, ILogger<ProductsController> log)
    {
        _db = db; _repo = repo; _log = log;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    public async Task<IActionResult> Index(string? search = null, int page = 1, int pageSize = 25)
    {
        var query = _db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(p => p.SKU.Contains(search) || p.Title.Contains(search));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreatedOn)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        ViewBag.Total = total;
        ViewBag.Page  = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Search = search;
        return View(items);
    }

    [HttpGet] public IActionResult Create() => View("Edit", new ProductFormViewModel());

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await _repo.GetWithImagesAsync(id);
        if (p is null) return NotFound();
        return View(new ProductFormViewModel
        {
            ProductId    = p.ProductId,
            SKU          = p.SKU,
            Barcode      = p.Barcode,
            Title        = p.Title,
            Description  = p.Description,
            Brand        = p.Brand,
            Category     = p.Category,
            MRP          = p.MRP,
            SellingPrice = p.SellingPrice,
            WeightGrams  = p.WeightGrams,
            HSNCode      = p.HSNCode,
            GSTPercent   = p.GSTPercent,
            ImageUrlsCsv = string.Join("\n", p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl))
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProductFormViewModel vm)
    {
        if (!ModelState.IsValid) return View("Edit", vm);

        // Section 5: validate duplicates server-side via SP
        var conflicts = await _repo.CheckDuplicatesAsync(vm.SKU, vm.Barcode,
            vm.ProductId == 0 ? null : vm.ProductId);

        if (conflicts.Any())
        {
            foreach (var c in conflicts)
                ModelState.AddModelError(c.Field == "SKU" ? nameof(vm.SKU) : nameof(vm.Barcode),
                    $"Duplicate {c.Field}: already used by '{c.ConflictingTitle}' (Product #{c.ConflictingProductId}, owner #{c.OwnerUserId})");
            return View("Edit", vm);
        }

        Product product;
        if (vm.ProductId == 0)
        {
            product = new Product { OwnerUserId = CurrentUserId };
            _db.Products.Add(product);
        }
        else
        {
            product = await _db.Products.Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.ProductId == vm.ProductId)
                ?? throw new InvalidOperationException("Product not found");
            product.UpdatedOn = DateTime.UtcNow;
        }

        product.SKU          = vm.SKU.Trim();
        product.Barcode      = string.IsNullOrWhiteSpace(vm.Barcode) ? null : vm.Barcode.Trim();
        product.Title        = vm.Title.Trim();
        product.Description  = vm.Description;
        product.Brand        = vm.Brand;
        product.Category     = vm.Category;
        product.MRP          = vm.MRP;
        product.SellingPrice = vm.SellingPrice;
        product.WeightGrams  = vm.WeightGrams;
        product.HSNCode      = vm.HSNCode;
        product.GSTPercent   = vm.GSTPercent;

        // images: replace whole list with the textarea contents
        var urls = (vm.ImageUrlsCsv ?? "")
            .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        product.Images.Clear();
        for (int i = 0; i < urls.Count; i++)
        {
            product.Images.Add(new ProductImage
            {
                ImageUrl  = urls[i],
                SortOrder = i,
                IsPrimary = i == 0
            });
        }

        await _db.SaveChangesAsync();
        TempData["msg"] = $"Saved product '{product.Title}'";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.Products.FindAsync(id);
        if (p is null) return NotFound();
        _db.Products.Remove(p);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // ---------- Bulk Upload (Section 3 module) ----------
    [HttpGet] public IActionResult BulkUpload() => View();

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> BulkUpload(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("", "Choose a CSV file");
            return View();
        }

        var results = new List<ProductImportResult>();
        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null };
        using var csv = new CsvReader(reader, cfg);

        var rows = csv.GetRecords<ProductImportRow>().ToList();
        int line = 1;
        foreach (var row in rows)
        {
            line++;
            var r = new ProductImportResult { RowNumber = line, SKU = row.SKU };
            try
            {
                var conflicts = await _repo.CheckDuplicatesAsync(row.SKU, row.Barcode);
                if (conflicts.Any())
                {
                    r.Success = false;
                    r.Error   = $"Duplicate {conflicts[0].Field}";
                }
                else
                {
                    var p = new Product
                    {
                        OwnerUserId  = CurrentUserId,
                        SKU          = row.SKU.Trim(),
                        Barcode      = row.Barcode,
                        Title        = row.Title,
                        Description  = row.Description,
                        Brand        = row.Brand,
                        Category     = row.Category,
                        MRP          = row.MRP,
                        SellingPrice = row.SellingPrice,
                        WeightGrams  = row.WeightGrams,
                        HSNCode      = row.HSNCode,
                        GSTPercent   = row.GSTPercent
                    };
                    int seq = 0;
                    foreach (var u in row.ImageUrls)
                        p.Images.Add(new ProductImage { ImageUrl = u, SortOrder = seq++, IsPrimary = seq == 1 });

                    _db.Products.Add(p);
                    await _db.SaveChangesAsync();
                    r.Success = true;
                    r.ProductId = p.ProductId;
                }
            }
            catch (Exception ex)
            {
                r.Success = false;
                r.Error   = ex.Message;
                _log.LogWarning(ex, "Import error row {Line}", line);
            }
            results.Add(r);
        }
        return View("BulkUploadResult", results);
    }
}
