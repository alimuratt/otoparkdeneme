using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SitePass.Infrastructure.Data;
using SitePass.Infrastructure.Services;
using SitePass.Api.Hubs;
using System;
using System.Text;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register background hosted service for deactivating guest vehicles
builder.Services.AddHostedService<VehicleCleanupService>();
builder.Services.AddHostedService<SüreTakipJob>();

// Register ExcelManager as a singleton
builder.Services.AddSingleton<ExcelManager>();
builder.Services.AddSingleton<PushNotificationService>();

// Configure dynamic DbContext based on appsettings.json
var databaseProvider = builder.Configuration.GetValue<string>("DatabaseProvider");
if (string.Equals(databaseProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    builder.Services.AddDbContext<SitePassDbContext>(options =>
        options.UseNpgsql(connectionString, b => b.MigrationsAssembly("SitePass.Infrastructure")));
}
else if (string.Equals(databaseProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("SqlServer");
    builder.Services.AddDbContext<SitePassDbContext>(options =>
        options.UseSqlServer(connectionString, b => b.MigrationsAssembly("SitePass.Infrastructure")));
}
else if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("Sqlite");
    builder.Services.AddDbContext<SitePassDbContext>(options =>
        options.UseSqlite(connectionString, b => b.MigrationsAssembly("SitePass.Infrastructure")));
}
else
{
    throw new InvalidOperationException($"Unsupported database provider: {databaseProvider}");
}

// OtoparkDbContext (SQL Server) registration removed in favor of ExcelManager

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var keyString = jwtSettings.GetValue<string>("Key") ?? "SuperSecretKeyForSitePassProjectAuthentication2026!";
var key = Encoding.ASCII.GetBytes(keyString);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.GetValue<string>("Issuer") ?? "SitePassApi",
        ValidateAudience = true,
        ValidAudience = jwtSettings.GetValue<string>("Audience") ?? "SitePassClient",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    
    // Support SignalR Authentication via Query String
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// Configure SignalR
builder.Services.AddSignalR();

// Add CORS - open policy for development to prevent any origin mismatch
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Seed Database automatically on startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<SitePassDbContext>();
        DbSeeder.Seed(dbContext);

        // Sync SQLite active vehicles back to Excel to prevent state desync
        var excelManager = scope.ServiceProvider.GetRequiredService<ExcelManager>();
        
        var activeVehicles = dbContext.Vehicles
            .Include(v => v.Resident)
            .Where(v => v.IsActive)
            .ToList();

        var excelList = excelManager.GetBeyazListeAsync().GetAwaiter().GetResult();

        foreach (var vehicle in activeVehicles)
        {
            var existsInExcel = excelList.Any(row => row.Plaka == vehicle.Plate && row.IsActive);
            if (!existsInExcel)
            {
                var ownerName = vehicle.Resident != null ? $"{vehicle.Resident.FirstName} {vehicle.Resident.LastName}" : "Site Sakini";
                var blockDaire = vehicle.Resident != null ? $"{vehicle.Resident.BlockNo} BLOK D:{vehicle.Resident.ApartmentNo}" : "Bilinmiyor";

                excelManager.AddGuestVehicleAsync(new SitePass.Core.Entities.BeyazListe
                {
                    Plaka = vehicle.Plate,
                    SahipAdSoyad = ownerName,
                    BlokDaire = blockDaire,
                    IsGuest = vehicle.IsGuest,
                    IsActive = true,
                    ExpireDate = vehicle.ExpireDate
                }).GetAwaiter().GetResult();
                
                Console.WriteLine($"[Sync] SQLite'tan Excel'e senkronize edilen plaka: {vehicle.Plate}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred during startup seeding/sync: {ex.Message}");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationHub>("/hub/notifications");

// Serve frontend static files from root directory
var frontendPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../.."));
app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendPath),
    RequestPath = ""
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendPath),
    RequestPath = ""
});

app.Run();
