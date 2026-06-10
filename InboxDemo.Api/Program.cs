using Dapper;
using InboxDemo.Common.Models;
using MassTransit;
using Npgsql;
using Scalar.AspNetCore;
using System.ComponentModel.DataAnnotations;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddMassTransit(bus =>
{
    bus.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri("rabbitmq://localhost:5673/"), h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.UseMessageRetry(r => r.Interval(3, 1000));
        cfg.UseInMemoryOutbox(context);
    });
});

var frontendOrigin = builder.Configuration["FrontendOrigin"] ?? "https://localhost:5001";

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(frontendOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.UseHttpsRedirection();

var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSQL is not configured.");

app.MapPost("/api/orders", async (
    CreateOrderRequest request,
    IPublishEndpoint publishEndpoint) =>
{
    // 手动验证（Minimal API 不自动验证 record 参数）
    var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
    var context = new ValidationContext(request);
    if (!Validator.TryValidateObject(request, context, validationResults, true))
    {
        var errors = validationResults.Select(v => v.ErrorMessage).ToArray();
        return Results.BadRequest(new { Errors = errors });
    }

    var orderCreated = new OrderCreated
    {
        OrderId = Guid.NewGuid(),
        CustomerName = request.CustomerName,
        Amount = request.Amount,
        CreatedAt = DateTime.UtcNow
    };

    await publishEndpoint.Publish(orderCreated);

    return Results.Ok(new { orderCreated.OrderId, Message = "Order created event published" });
})
.WithName("CreateOrder");

app.MapGet("/api/inbox-messages", async (int? page, int? pageSize) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 100, 1, 500);
    var offset = (p - 1) * ps;

    using var connection = new NpgsqlConnection(connectionString);

    var sql = @"
        SELECT id AS Id,
               message_type AS MessageType,
               payload::text AS Payload,
               received_on_utc AS ReceivedOnUtc,
               processed_on_utc AS ProcessedOnUtc,
               error AS Error,
               retry_count AS RetryCount
        FROM inbox_messages
        ORDER BY received_on_utc DESC
        LIMIT @PageSize OFFSET @Offset
    ";

    var messages = await connection.QueryAsync<InboxMessage>(sql, new { PageSize = ps, Offset = offset });
    return Results.Ok(messages);
})
.WithName("GetInboxMessages");

app.Run();

public record CreateOrderRequest
{
    [Required(ErrorMessage = "Customer name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Customer name must be 1-200 characters")]
    public string CustomerName { get; init; } = "";

    [Required(ErrorMessage = "Amount is required")]
    [Range(0.01, 1_000_000, ErrorMessage = "Amount must be between 0.01 and 1,000,000")]
    public decimal Amount { get; init; }
}
