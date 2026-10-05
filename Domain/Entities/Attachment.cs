using Domain.Common;
using Domain.Interfaces;

namespace Domain.Entities
{
    public class Attachment : BaseEntity
    {
        public Guid AttachmentId { get; set; }
        public string OwnerType { get; set; } // e.g. "IncomingCorrespondence"

        public long OwnerId { get; set; }

        public string FileName { get; set; }

        public string FileExtension { get; set; } // .pdf, .jpg, etc.

        public string FilePath { get; set; }

        public long FileSize { get; set; }

        public string ContentType { get; set; }

        public string? Description { get; set; }

        public DateTime UploadedAt { get; set; }

    }
}
