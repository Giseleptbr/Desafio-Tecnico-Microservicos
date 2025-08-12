var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Inventory API OK");
app.MapGet("/api/products", () => Results.Ok(Array.Empty<object>()));

app.Run();
