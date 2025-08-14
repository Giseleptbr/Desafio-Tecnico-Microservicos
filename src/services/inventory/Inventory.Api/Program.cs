using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ================== SERVICES (ANTES do Build) ==================

// DB (SQLite)
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// JWT
var jwt = builder.Configuration.GetSection("Jwt");
var jwtIssuer   = jwt["Issuer"]!;
var jwtAudience = jwt["Audience"]!;
var jwtKey      = jwt["Key"]!;
var keyBytes    = Encoding.UTF8.GetBytes(jwtKey);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer          = jwtIssuer,
            ValidAudience        = jwtAudience,
            IssuerSigningKey     = new SymmetricSecurityKey(keyBytes),
            ValidateIssuer       = true,
            ValidateAudience     = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime     = true,
            ClockSkew            = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ================== BUILD ==================
var app = builder.Build();

// ================== MIDDLEWARES ==================
app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

// ================== ENDPOINTS (protegidos) ==================
app.MapGet("/api/products", async (InventoryDbContext db) =>
    await db.Products.ToListAsync()).RequireAuthorization();

app.MapPost("/api/products", async (InventoryDbContext db, Product product) =>
{
    db.Products.Add(product);
    await db.SaveChangesAsync();
    return Results.Created($"/api/products/{product.Sku}", product);
}).RequireAuthorization();

app.MapGet("/api/products/{sku}", async (InventoryDbContext db, string sku) =>
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == sku);
    return product is not null ? Results.Ok(product) : Results.NotFound();
}).RequireAuthorization();

app.MapPatch("/api/products/{sku}", async (InventoryDbContext db, string sku, Product updated) =>
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == sku);
    if (product is null) return Results.NotFound();

    // atualize o que precisar
    product.Price     = updated.Price;
    product.Quantity  = updated.Quantity;
    product.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(product);
}).RequireAuthorization();

app.MapPost("/api/inventory/validate", async (InventoryDbContext db, Sale sale) =>
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == sale.Product);
    var ok = product is not null && product.Quantity >= sale.Quantity;
    return Results.Ok(ok);
}).AllowAnonymous();

// -------- endpoint simples para emitir token (para testes) --------
app.MapPost("/api/auth/login", (string username) =>
{
    var claims = new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim("role", "api-user")
    };

    var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(int.Parse(jwt["ExpiryMinutes"] ?? "60")),
        signingCredentials: creds);

    var jwtString = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { token = jwtString });
}).AllowAnonymous();

// ================== RABBITMQ CONSUMER ==================
var factory    = new ConnectionFactory { HostName = "localhost", UserName = "guest", Password = "guest" };
var connection = factory.CreateConnection();
var channel    = connection.CreateModel();

channel.ExchangeDeclare("ecommerce.sales", ExchangeType.Fanout, durable: true);
channel.QueueDeclare(queue: "inventory.debit", durable: true, exclusive: false, autoDelete: false);
channel.QueueBind(queue: "inventory.debit", exchange: "ecommerce.sales", routingKey: "");

var consumer = new EventingBasicConsumer(channel);
consumer.Received += async (_, ea) =>
{
    try
    {
        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
        var sale = JsonSerializer.Deserialize<Sale>(json);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        if (sale is not null)
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == sale.Product);
            if (product != null)
            {
                product.Quantity  = Math.Max(0, product.Quantity - sale.Quantity);
                product.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                Console.WriteLine($"[Inventory] Debitado {sale.Quantity} do SKU {sale.Product}");
            }
            else
            {
                Console.WriteLine($"[Inventory] SKU não encontrado: {sale.Product}");
            }
        }

        channel.BasicAck(ea.DeliveryTag, false);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Inventory] Erro: {ex.Message}");
        channel.BasicNack(ea.DeliveryTag, false, requeue: false);
    }
};

channel.BasicConsume(queue: "inventory.debit", autoAck: false, consumer: consumer);

// graceful shutdown
AppDomain.CurrentDomain.ProcessExit += (_, __) =>
{
    try { channel?.Close(); } catch { }
    try { connection?.Close(); } catch { }
};

// health
app.MapGet("/_health", () => Results.Ok("ok")).AllowAnonymous();

// ================== RUN ==================
app.Run();

// tipos locais
public record Sale(int Id, string Product, int Quantity, decimal Price);
