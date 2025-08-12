namespace Sales.Api.Events;

public class OrderConfirmed
{
    public string OrderId { get; set; } = default!;
    public string OrderNumber { get; set; } = default!;
    public List<OrderItemEvt> Items { get; set; } = new();
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class OrderItemEvt
{
    public string Sku { get; set; } = default!;
    public int Qty { get; set; }
}
