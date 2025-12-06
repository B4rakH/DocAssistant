using DocAssistant.Gateway.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Data
{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Models.Document> Documents { get; set; }
        public DbSet<Chat> Chats { get; set; }
        public DbSet<ChatDocument> ChatDocuments { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Configure Composite Key for Junction Table
            modelBuilder.Entity<ChatDocument>()
                .HasKey(cd => new { cd.ChatId, cd.DocumentId });

            // 2. Configure Relationships for Junction Table
            modelBuilder.Entity<ChatDocument>()
                .HasOne(cd => cd.Chat)
                .WithMany(c => c.ChatDocuments)
                .HasForeignKey(cd => cd.ChatId);

            modelBuilder.Entity<ChatDocument>()
                .HasOne(cd => cd.Document)
                .WithMany() // Document doesn't track its chats (keeps it clean)
                .HasForeignKey(cd => cd.DocumentId);

            // 3. Configure Chat Messages (Cascade Delete)
            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.Chat)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ChatId)
                .OnDelete(DeleteBehavior.Cascade); // Delete Chat -> Delete Messages
        }

    }
}
