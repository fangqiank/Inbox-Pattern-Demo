using Dapper;
using InboxDemo.Common.Models;
using MassTransit;
using Npgsql;
using Scalar.AspNetCore;

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
    ?? "Host=127.0.0.1;Port=5433;Database=inbox_demo;Username=postgres;Password=postgres";

app.MapPost("/api/orders", async (
    CreateOrderRequest request,
    IPublishEndpoint publishEndpoint) =>
{
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

app.MapGet("/api/inbox-messages", async () =>
{
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
        LIMIT 100
    ";

    var messages = await connection.QueryAsync<InboxMessage>(sql);
    return Results.Ok(messages);
})
.WithName("GetInboxMessages");

app.Run();

public record CreateOrderRequest(string CustomerName, decimal Amount);
