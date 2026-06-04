namespace Agitprop.Web.Api.DTOs.Responses;

public class DomainStatDto
{
    public required string Domain { get; set; }
    public int Count { get; set; }
    public double Percent { get; set; }
}

public class EntityDomainStatsResponse
{
    public List<DomainStatDto> Domains { get; set; } = [];
    public int TotalCount { get; set; }
}
