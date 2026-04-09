public class EntityDetailsDto: EntityDto
{
public required Dictionary<DateOnly, int> MentionsCountByDate { get; set; }
public required int TotalMentions { get; set; }
}
