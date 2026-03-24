using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Common.Models
{
    [DataContract]
    public class DocumentDto
    {
        [DataMember] public string? Title { get; set; }
        [DataMember] public string? Content { get; set; }
        [DataMember] public string? DocumentType { get; set; }
        [DataMember] public DateTime ValidFrom { get; set; }
        [DataMember] public DateTime? ValidTo { get; set; }
    }

    [DataContract]
    public class DocumentVersion
    {
        [DataMember] public string? DocumentId { get; set; }
        [DataMember] public string? Title { get; set; }
        [DataMember] public string? DocumentType { get; set; }
        [DataMember] public int VersionNumber { get; set; }
        [DataMember] public DateTime ValidFrom { get; set; }
        [DataMember] public DateTime? ValidTo { get; set; }
        [DataMember] public DateTime CreatedAt { get; set; }
        [DataMember] public string? Content { get; set; }
        [DataMember] public List<DocumentSection> Sections { get; set; } = new();
    }

    [DataContract]
    public class DocumentSection
    {
        [DataMember] public string? SectionId { get; set; }
        [DataMember] public string? DocumentId { get; set; }
        [DataMember] public string? Title { get; set; }
        [DataMember] public string? Content { get; set; }
        [DataMember] public SectionType Type { get; set; }
        [DataMember] public int OrderIndex { get; set; }
    }

    public enum SectionType { Clan, Stav, Tacka }
}
