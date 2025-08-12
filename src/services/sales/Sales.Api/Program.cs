var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Sales API OK");
app.MapPost("/api/orders", () => Results.Created("/api/orders/1", new { orderNumber = "TEST-1", status = "Confirmed" }));

app.Run();
