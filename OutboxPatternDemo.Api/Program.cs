using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddSingleton<OutboxSaveChangesInterceptor>();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var interceptor = serviceProvider.GetRequiredService<OutboxSaveChangesInterceptor>();

    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .AddInterceptors(interceptor);
});


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/users", async (CreateUserRequest request, AppDbContext dbContext) =>
{
    var user = new OutboxPatternDemo.Domain.User(Guid.NewGuid(), request.Username);
    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Username });
});

app.MapPost("/api/users/{followerId}/follow/{followedId}", async (
    Guid followerId,
    Guid followedId,
    AppDbContext dbContext) =>
{
    var follower = await dbContext.Users
        .Include(u => u.Following)
        .FirstOrDefaultAsync(u => u.Id == followerId);

    var followed = await dbContext.Users.FindAsync(followedId);

    if (follower is null || followed is null)
        return Results.NotFound();

    try
    {
        follower.Follow(followed);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new { message = $"{follower.Username} started following {followed.Username}" });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/users", async (AppDbContext dbContext) =>
{
    var users = await dbContext.Users
        .OrderBy(u => u.Username)
        .Select(u => new { u.Id, u.Username })
        .ToListAsync();

    return Results.Ok(users);
});

app.MapGet("/api/outbox/messages", async (AppDbContext dbContext) =>
{
    var messages = await dbContext.OutboxMessages
        .OrderByDescending(m => m.CreatedOnUtc)
        .Take(20)
        .ToListAsync();

    return Results.Ok(messages);
});

app.MapGet("/api/inbox/messages", async (AppDbContext dbContext) =>
{
    var messages = await dbContext.InboxMessages
        .OrderByDescending(m => m.ProcessedOnUtc)
        .Take(20)
        .Select(m => new
        {
        m.Id,
        m.MessageId,
        m.Name,
        m.HandlerName,
        m.OccurredOnUtc,
        m.ProcessedOnUtc,
        m.Error
        })
        .ToListAsync();

    return Results.Ok(messages);
});

app.MapGet("/api/inbox/messages/{messageId}", async (Guid messageId, AppDbContext dbContext) =>
{
    var messages = await dbContext.InboxMessages
        .Where(m => m.MessageId == messageId)
        .Select(m => new
        {
        m.Id,
        m.HandlerName,
        m.ProcessedOnUtc,
        ProcessingTime = m.ProcessedOnUtc.HasValue ?
                    (m.ProcessedOnUtc.Value - m.OccurredOnUtc).TotalMilliseconds : (double?)null
        })
        .ToListAsync();

    return Results.Ok(messages);
});

// 获取处理统计
app.MapGet("/api/inbox/stats", async (AppDbContext dbContext) =>
{
    var stats = await dbContext.InboxMessages
        .GroupBy(m => new { m.Name, m.HandlerName })
        .Select(g => new
        {
            EventType = g.Key.Name,
            Handler = g.Key.HandlerName,
            Total = g.Count(),
            Failed = g.Count(m => m.Error != null),
            AverageProcessingTime = g.Where(m => m.ProcessedOnUtc.HasValue)
                .Average(m => (m.ProcessedOnUtc!.Value - m.OccurredOnUtc).TotalMilliseconds)
        })
        .ToListAsync();

    return Results.Ok(stats);
});

app.Run();

public record CreateUserRequest(string Username);