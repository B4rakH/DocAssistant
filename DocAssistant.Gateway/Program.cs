using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Services;
using DotNetEnv;
using MassTransit;
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

            builder.Services.AddMassTransit(x =>
            {
                x.AddConsumer<DocumentResultConsumer>();

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(new Uri(Environment.GetEnvironmentVariable("RABBITMQ_URI") ?? throw new Exception("RabbitMQ Uri cannot found")), h =>
                    {
                        h.Username(Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? throw new Exception("Username cannot found"));
                        h.Password(Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? throw new Exception("Password cannot found"));
                    });

                    cfg.UseRawJsonDeserializer();

                    
                    cfg.ReceiveEndpoint("documents.results", e =>
                    {
                        e.ConfigureConsumer<DocumentResultConsumer>(context);

                        //e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                    });
                });
            });

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
