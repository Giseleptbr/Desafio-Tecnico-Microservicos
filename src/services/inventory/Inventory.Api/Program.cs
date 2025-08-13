using RabbitMQ.Client;
using RabbitMQ.Client.Events;   // <- necessário para EventingBasicConsumer
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;       // <- seu DbContext
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Inventory.Api.Models;



var builder = WebApplication.CreateBuilder(args);

// ==== JWT (Inventory) ====
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtIssuer = jwtSection["Issuer"]!;
var jwtAudience = jwtSection["Audience"]!;
var jwtKey = jwtSection["Key"]!;
var jwtKeyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();


// (config do EF/serviços aqui)

var app = builder.Build();

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));


app.UseAuthentication();
app.UseAuthorization();

//minimal API 

app.MapGet("/api/products", async (InventoryDbContext db) =>
{
    return await db.Products.ToListAsync();
}).RequireAuthorization();

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

app.MapPatch("/api/products/{sku}", async (InventoryDbContext db, string sku, Product updatedProduct) =>
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == sku);
    if (product is null) return Results.NotFound();

    product.Quantity = updatedProduct.Quantity;
    await db.SaveChangesAsync();
    return Results.Ok(product);
}).RequireAuthorization();

app.MapPost("/api/inventory/validate", async (InventoryDbContext db, Sale sale) =>
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == sale.Product);
    return product is not null && product.Quantity >= sale.Quantity
        ? Results.Ok(true)
        : Results.Ok(false);
}).RequireAuthorization();


// ===== CONSUMER RABBITMQ (6.8.1 síncrono) =====
var factory = new ConnectionFactory { HostName = "localhost", UserName = "guest", Password = "guest" };
var connection = factory.CreateConnection();
var channel = connection.CreateModel();

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
                product.Quantity = Math.Max(0, product.Quantity - sale.Quantity);
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

AppDomain.CurrentDomain.ProcessExit += (_, __) =>
{
    try { channel?.Close(); } catch { }
    try { connection?.Close(); } catch { }
};

app.Run();

// >>> DECLARE O TIPO **APÓS** TODOS OS STATEMENTS <<<
public record Sale(int Id, string Product, int Quantity, decimal Price);