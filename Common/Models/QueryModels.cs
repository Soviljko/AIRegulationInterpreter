using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Common.Models
{
    [DataContract]
    public class QueryRequest
    {
        [DataMember] public string? Question { get; set; }
        [DataMember] public DateTime? ContextDate { get; set; }
        [DataMember] public string? OrganizationType { get; set; }
    }

    [DataContract]
    public class QueryResponse
    {
        [DataMember] public string? Explanation { get; set; }
        [DataMember] public List<CitationDto> Citations { get; set; } = new();
        [DataMember] public int Confidence { get; set; }
        [DataMember] public bool HasSufficientInfo { get; set; }
    }

    [DataContract]
    public class CitationDto
    {
        [DataMember] public string? SectionId { get; set; }
        [DataMember] public string? DocumentTitle { get; set; }
        [DataMember] public string? SectionTitle { get; set; }
        [DataMember] public string? RelevantText { get; set; }
    }
}
