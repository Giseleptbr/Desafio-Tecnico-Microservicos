namespace Sales.Api.Models;

public class OrderRequest
{
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public string Sku { get; set; } = default!;
    public int Qty { get; set; }
}

public class OrderResponse
{
    public string OrderNumber { get; set; } = default!;
    public string Status { get; set; } = default!;
}

public class ValidationRequest
{
    public List<ValidationItem> Items { get; set; } = new();
}

public class ValidationItem
{
    public string Sku { get; set; } = default!;
    public int Qty { get; set; }
}

public class ValidationResponse
{
    public bool IsAvailable { get; set; }
    public List<string> Unavailable { get; set; } = new();
}
