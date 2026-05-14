using DocAssistant.Gateway.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Data
{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Document> Documents { get; set; }
        public DbSet<Chat> Chats { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Guid primary keys to be client-generated (not database-generated)
            modelBuilder.Entity<Document>()
                .Property(d => d.Id)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<Chat>()
                .Property(c => c.Id)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<ChatMessage>()
                .Property(m => m.Id)
                .ValueGeneratedOnAdd();

            // Configure enums to store as integers (fix PostgreSQL varchar issue)
            modelBuilder.Entity<ChatMessage>()
                .Property(m => m.Role)
                .HasConversion<int>();

            modelBuilder.Entity<Document>()
                .Property(d => d.Status)
                .HasConversion<int>();

            // Configure Document -> Chat relationship (Cascade Delete)
            modelBuilder.Entity<Models.Document>()
                .HasOne(d => d.Chat)
                .WithMany(c => c.Documents)
                .HasForeignKey(d => d.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure ChatMessage -> Chat relationship (Cascade Delete)
            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.Chat)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure indexes (for better query performance)
            modelBuilder.Entity<Document>()
                .HasIndex(d => d.ChatId);

            // Composite index for document validation queries (ChatId + Status)
            modelBuilder.Entity<Document>()
                .HasIndex(d => new { d.ChatId, d.Status })
                .HasDatabaseName("IX_Documents_ChatId_Status");

            modelBuilder.Entity<ChatMessage>()
                .HasIndex(m => m.ChatId);

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.Status);
        }

    }
}
