using DocAssistant.Gateway.Common;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Repositories;
using DocAssistant.Gateway.Services;
using DotNetEnv;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

namespace DocAssistant.Gateway
{
    public class Program
    {
        public static async Task Main(string[] args)
        {

            Env.Load();

            var builder = WebApplication.CreateBuilder(args);

            string DbConnectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"]!;
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(DbConnectionString));

            // Add services to the container.
            builder.Services.AddScoped<IChatService, ChatService>();
            builder.Services.AddSingleton<IMinioService, MinioService>();

            builder.Services.AddMassTransit(x =>
            {
                x.AddConsumer<DocumentResultConsumer>();

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(new Uri(builder.Configuration["RABBITMQ_URI"]!), h =>
                    {
                        h.Username(builder.Configuration["RABBITMQ_USERNAME"]!);
                        h.Password(builder.Configuration["RABBITMQ_PASSWORD"]!);
                    });

                    cfg.UseRawJsonDeserializer();

                    
                    cfg.ReceiveEndpoint(QueueNames.fileUploadResultQueue, e =>
                    {
                        e.ConfigureConsumer<DocumentResultConsumer>(context);

                        //e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                    });
                });
            });

            builder.Services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
            builder.Services.AddScoped<IChatRepository, ChatRepository>();
            builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Ensure MinIO bucket exists
            using (var scope = app.Services.CreateScope())
            {
                var minioService = scope.ServiceProvider.GetRequiredService<IMinioService>();
                await minioService.EnsureBucketExistsAsync();
            }

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
