using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.KitchenOps;

using RestaurantSim.Application.AutoManager;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Application.Common;
using RestaurantSim.Application.TickProcessing;

using RestaurantSim.Infrastructure.BackgroundServices;
using RestaurantSim.Infrastructure.Persistence;
using RestaurantSim.Infrastructure.Presets;
using RestaurantSim.Infrastructure.Random;
using RestaurantSim.Infrastructure.SignalR;

using RestaurantSim.AutoManager;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.AutoManager.Strategies.TableLayout;
using RestaurantSim.AutoManager.Strategies.TaskPriority;
using RestaurantSim.AutoManager.Strategies.WaiterAction;

using MediatR;
using FluentValidation;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IRestaurantRepository, InMemoryRestaurantRepository>();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(SeatGroupCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(RestaurantHub).Assembly); // Infrastructure: SignalR notification handlers
}); builder.Services.AddValidatorsFromAssembly(typeof(SeatGroupCommandValidator).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RestaurantLockBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Client", policy => policy
        .WithOrigins("https://localhost:7280", "http://localhost:5280")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});
builder.Services.AddSingleton<IRandomProvider, SystemRandomProvider>();
builder.Services.AddSingleton<ArrivalGroupGenerator>();

builder.Services.AddSingleton<IPresetRepository<RestaurantConfig>>(
    new InMemoryPresetRepository<RestaurantConfig>(
        new Dictionary<string, RestaurantConfig> { ["default_diner"] = DefaultDinerConfigSeed.Create() }));

builder.Services.AddSingleton<IMenuRepository>(
    new InMemoryMenuRepository(DefaultDinerMenuSeed.Create()));

builder.Services.AddTransient<RestaurantTickProcessor>();
builder.Services.AddHostedService<SimulationTickService>();

builder.Services.AddSingleton<ILayoutRepository>(
    new InMemoryLayoutRepository(new Dictionary<string, Func<RestaurantLayout>>
    {
        ["default_diner_layout"] = DefaultDinerLayoutSeed.Create
    }));

builder.Services.AddSingleton<ISnapshotStore, InMemorySnapshotStore>();

builder.Services.AddScoped<ISeatingStrategy, DefaultSeatingStrategy>();
builder.Services.AddScoped<IAutoManagerRunner, AutoManagerProcessor>();
builder.Services.AddScoped<IStaffTaskPriorityStrategy, DefaultStaffTaskPriorityStrategy>();
builder.Services.AddScoped<IWaiterActionStrategy, DefaultWaiterActionStrategy>();
builder.Services.AddScoped<ITableLayoutStrategy, DefaultTableLayoutStrategy>();

var app = builder.Build();

app.UseCors("Client");

app.MapHub<RestaurantHub>("/hubs/restaurant");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
