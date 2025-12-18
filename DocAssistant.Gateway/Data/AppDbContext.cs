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
            modelBuilder.Entity<Models.Document>()
                .HasIndex(d => d.ChatId);

            modelBuilder.Entity<ChatMessage>()
                .HasIndex(m => m.ChatId);

            modelBuilder.Entity<Models.Document>()
                .HasIndex(d => d.Status);
        }

    }
}
