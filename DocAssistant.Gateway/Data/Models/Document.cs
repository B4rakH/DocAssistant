using DocAssistant.Gateway.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DocAssistant.Gateway.Data.Models
{
    public class Document
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [ForeignKey(nameof(Chat))]
        public Guid ChatId { get; set; }

        [Required]
        [MaxLength(255)]
        public required string FileName { get; set; }

        [Required]
        public required string FilePath { get; set; }

        public long FileSize { get; set; }

        [MaxLength(100)]
        public string ContentType { get; set; } = "application/pdf";

        public DocumentStatus Status { get; set; } = DocumentStatus.Loading;

        public string? FailureReason { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public Chat Chat { get; set; } = null!;
    }
}
