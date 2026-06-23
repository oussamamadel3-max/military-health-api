using Domain.Common;
using Domain.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class Attachment : BaseEntity
    {
        public Guid AttachmentId { get; set; }
        [Required]
        public string OwnerType { get; set; } // e.g. "IncomingCorrespondence"

        public long OwnerId { get; set; }

        [Required]
        public string FileName { get; set; }

        public string FileExtension { get; set; } // .pdf, .jpg, etc.

        [Required]
        public string FilePath { get; set; }

        public long FileSize { get; set; }

        public string ContentType { get; set; }

        public string? Description { get; set; }

        public bool IsProfilePhoto { get; set; } = false;

        public DateTime UploadedAt { get; set; }

    }
}


