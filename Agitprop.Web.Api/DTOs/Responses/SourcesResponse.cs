namespace Agitprop.Web.Api.DTOs.Responses;

public class SourceStatDto
{
    public required string Domain { get; set; }
    public int Count { get; set; }
    public double Percent { get; set; }
}

public class SourcesResponse
{
    public List<SourceStatDto> Sources { get; set; } = [];
    public int TotalCount { get; set; }
}
