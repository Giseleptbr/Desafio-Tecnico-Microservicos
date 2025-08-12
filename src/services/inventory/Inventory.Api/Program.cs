using RabbitMQ.Client;
using RabbitMQ.Client.Events;   // <- necessário para EventingBasicConsumer
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;       // <- seu DbContext


var builder = WebApplication.CreateBuilder(args);

// (config do EF/serviços aqui)

var app = builder.Build();

// (EnsureCreated e seus endpoints aqui)

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