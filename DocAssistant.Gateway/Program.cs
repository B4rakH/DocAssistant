using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Services;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);


            // ===== DELETE AFTER POSTGRESQL CONFIG START =====
            // Temporary InMemory Database for testing
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("DocAssistantInMemoryDb"));
            // ===== DELETE AFTER POSTGRESQL CONFIG END =====



            // Add services to the container.
            builder.Services.AddScoped<IChatService, ChatService>();
            
            // Register RabbitMQ service
            builder.Services.AddSingleton<IRabbitMQService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RabbitMqService>>();
                return RabbitMqService.CreateServiceAsync(logger).GetAwaiter().GetResult();
            });

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();


            // ===== DELETE AFTER POSTGRESQL CONFIG START =====
            // Ensure InMemory database is created
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Database.EnsureCreated();
            }
            // ===== DELETE AFTER POSTGRESQL CONFIG END =====


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
