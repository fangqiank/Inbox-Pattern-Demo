using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Infrastructure;
using OutboxPatternDemo.Worker.EventHandlers;
using OutboxPatternDemo.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddTransient<SendNotificationOnUserFollowedHandler>();
builder.Services.AddTransient<UpdateFollowStatsHandler>();
builder.Services.AddTransient<AddToTimelineHandler>();

builder.Services.AddHostedService<InboxProcessor>();

var host = builder.Build();
host.Run();
