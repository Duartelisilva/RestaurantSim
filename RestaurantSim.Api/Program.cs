using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;

using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Application.Common;

using RestaurantSim.Infrastructure.Persistence;
using RestaurantSim.Infrastructure.SignalR;
using RestaurantSim.Infrastructure.Presets;
using RestaurantSim.Infrastructure.Random;

using MediatR;
using FluentValidation;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IRestaurantRepository, InMemoryRestaurantRepository>();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SeatGroupCommand).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(SeatGroupCommandValidator).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RestaurantLockBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRandomProvider, SystemRandomProvider>();
builder.Services.AddSingleton<ArrivalGroupGenerator>();

builder.Services.AddSingleton<IPresetRepository<RestaurantConfig>>(
    new InMemoryPresetRepository<RestaurantConfig>(
        new Dictionary<string, RestaurantConfig> { ["default_diner"] = DefaultDinerConfigSeed.Create() }));

var app = builder.Build();

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
