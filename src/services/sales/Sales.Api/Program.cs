// ===== USINGS =====
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Sales.Api.Events;
using Sales.Api.Models;

// ===== App setup =====
var builder = WebApplication.CreateBuilder(args);

// HttpClient -> Inventory
builder.Services.AddHttpClient("Inventory", c =>
{
    var baseUrl = builder.Configuration["Inventory:BaseUrl"] ?? "http://localhost:5136";
    c.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();
app.MapGet("/", () => "Sales API OK");

// ===== RabbitMQ (v6.8.1 – API síncrona) =====
var rmqFactory = new ConnectionFactory { HostName = "localhost" };
var rmqConnection = rmqFactory.CreateConnection();
var rmqChannel = rmqConnection.CreateModel();

// Exchange fanout para eventos de vendas
rmqChannel.ExchangeDeclare(exchange: "ecommerce.sales", type: ExchangeType.Fanout, durable: true);

// ===== Endpoints =====
app.MapPost("/api/orders", async (OrderRequest order, IHttpClientFactory http) =>
{
    if (order?.Items == null || order.Items.Count == 0)
        return Results.BadRequest(new { message = "Order must have at least one item." });

    // 1) Valida estoque no Inventory
    var client = http.CreateClient("Inventory");
    var validateBody = new ValidationRequest
    {
        Items = order.Items.Select(i => new ValidationItem { Sku = i.Sku, Qty = i.Qty }).ToList()
    };

    var resp = await client.PostAsJsonAsync("/api/inventory/validate", validateBody);
    if (!resp.IsSuccessStatusCode)
        return Results.Problem("Inventory validation failed.");

    var result = await resp.Content.ReadFromJsonAsync<ValidationResponse>();
    if (result is null || !result.IsAvailable)
        return Results.BadRequest(new { message = "Unavailable", result?.Unavailable });

    // 2) Monta evento de confirmação e publica no RabbitMQ
    var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}";
    var evt = new OrderConfirmed
    {
        OrderId = Guid.NewGuid().ToString(),
        OrderNumber = orderNumber,
        Items = order.Items.Select(i => new OrderItemEvt { Sku = i.Sku, Qty = i.Qty }).ToList(),
        OccurredAt = DateTime.UtcNow
    };

    var json = JsonSerializer.Serialize(evt);
    var body = Encoding.UTF8.GetBytes(json);

    var props = rmqChannel.CreateBasicProperties();
    props.Persistent = true;

    rmqChannel.BasicPublish(
        exchange: "ecommerce.sales",
        routingKey: "",
        basicProperties: props,
        body: body
    );

    // 3) Retorna sucesso
    return Results.Created($"/api/orders/{orderNumber}", new { orderNumber, status = "Confirmed" });
});

// Encerramento gracioso
AppDomain.CurrentDomain.ProcessExit += (_, __) =>
{
    try { rmqChannel?.Close(); } catch { }
    try { rmqConnection?.Close(); } catch { }
};

app.Run();
