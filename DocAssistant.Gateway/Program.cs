using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Services;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway
{
    public class Program
    {
        public static void Main(string[] args)
        {

            Env.Load();

            var builder = WebApplication.CreateBuilder(args);

            string DbConnectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"];
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(DbConnectionString));

            // Add services to the container.
            builder.Services.AddScoped<IChatService, ChatService>();

            // Register RabbitMQ service (concrete) and map IMessageProducer to same instance
            builder.Services.AddSingleton<RabbitMqService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RabbitMqService>>();
                return RabbitMqService.CreateServiceAsync(logger).GetAwaiter().GetResult();
            });

            // Map interface to the concrete instance
            builder.Services.AddSingleton<IMessageProducer>(sp => sp.GetRequiredService<RabbitMqService>());

            // Register background consumer that updates DB when results arrive
            builder.Services.AddHostedService<DocumentResultsConsumer>();

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
