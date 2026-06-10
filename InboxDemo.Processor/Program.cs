using InboxDemo.Processor;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

// 注册数据库连接
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? "Host=127.0.0.1;Port=5433;Database=inbox_demo;Username=postgres;Password=postgres";

builder.Services.AddSingleton(new InboxDatabase(connectionString));

// 注册消息处理器
builder.Services.AddTransient<OrderCreatedHandler>();
builder.Services.AddSingleton<MessageHandlerFactory>();

// 注册后台处理器
builder.Services.AddHostedService<InboxBackgroundProcessor>();

// 配置 MassTransit Consumer
builder.Services.AddMassTransit(x =>
{
    // 注册 Consumer
    x.AddConsumer<InboxConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri("rabbitmq://localhost:5673/"), h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ReceiveEndpoint("order-created-inbox", e =>
        {
            e.ConfigureConsumer<InboxConsumer>(context);

            // 配置重试策略
            e.UseMessageRetry(r => r.Interval(3, 1000));
        });
    });
});

var host = builder.Build();

// 初始化数据库
var inboxDb = host.Services.GetRequiredService<InboxDatabase>();
await inboxDb.InitializeAsync();

await host.RunAsync();
