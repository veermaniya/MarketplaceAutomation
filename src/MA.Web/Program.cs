using System.Text;
using Hangfire;
using Hangfire.SqlServer;
using MA.Automation;
using MA.Automation.Drivers;
using MA.Core.Interfaces;
using MA.Data;
using MA.Data.Repositories;
using MA.Data.Services;
using MA.Jobs;
using MA.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Hangfire.Dashboard;
using Hangfire.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

// -------------------- Logging --------------------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/ma-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

// -------------------- DB --------------------
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connStr));

// -------------------- Data Protection (persisted keys) --------------------
var keyDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keyDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDir))
    .SetApplicationName("MA.MarketplaceAutomation");

// -------------------- Auth (cookie for MVC, JWT for API) --------------------
builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
{
    var jwt = builder.Configuration.GetSection("Jwt");
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!))
    };
});

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("AdminOnly",   p => p.RequireRole("Admin"));
    o.AddPolicy("ManagerPlus", p => p.RequireRole("Admin", "Manager"));
});

// -------------------- App services --------------------
builder.Services.AddScoped<ICredentialProtector,   DataProtectionCredentialProtector>();
builder.Services.AddSingleton<IPasswordHasher,     Pbkdf2PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService,       JwtTokenService>();
builder.Services.AddScoped<IProductRepository,     ProductRepository>();
builder.Services.AddScoped<IRetryQueueService,     RetryQueueService>();

// Marketplace drivers + factory
builder.Services.AddScoped<FlipkartAutomation>();
builder.Services.AddScoped<AmazonAutomation>();
builder.Services.AddScoped<MeeshoAutomation>();
builder.Services.AddScoped<IMarketplaceAutomationFactory, MarketplaceAutomationFactory>();

// Jobs
builder.Services.AddScoped<MarketplaceJobs>();

// -------------------- Hangfire --------------------
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connStr, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));
builder.Services.AddHangfireServer(o => { o.WorkerCount = 4; });

// -------------------- MVC --------------------
builder.Services.AddControllersWithViews();

var app = builder.Build();

// -------------------- Pipeline --------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Hangfire dashboard - restrict to Admin
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthFilter() }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// -------------------- Recurring jobs --------------------
using (var scope = app.Services.CreateScope())
{
    RecurringJob.AddOrUpdate<MarketplaceJobs>(
        "retry-queue-worker",
        j => j.ProcessRetryQueueAsync(),
        Cron.Minutely);
    // Order fetch per active account is registered when accounts are created.
    // Hook that up in MarketplaceAccountsController.Save() if desired.
}

app.Run();

// Hangfire dashboard guard - only Admins via the cookie session
public class HangfireAuthFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext ctx)
    {
        var http = ctx.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true && http.User.IsInRole("Admin");
    }
}
