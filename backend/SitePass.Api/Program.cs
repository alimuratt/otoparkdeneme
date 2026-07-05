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
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred seeding the DB: {ex.Message}");
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

app.Run();
