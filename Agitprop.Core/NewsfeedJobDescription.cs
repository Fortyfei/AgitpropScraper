using Agitprop.Core;
using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed;

public class NewsfeedJobDescrpition : ScrapingJobDescription
{
    public PageContentType Type { get; init; }
}
