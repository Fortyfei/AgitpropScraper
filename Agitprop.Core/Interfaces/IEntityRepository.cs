using Agitprop.Core.Models;

namespace Agitprop.Core.Interfaces
{
    public interface IEntityRepository
    {
        IEnumerable<Entity> GetEntitiesAsync();
        IEnumerable<Article> GetMentioningArticlesAsync(string entityId, DateOnly from, DateOnly to);
        IDictionary<string,IEnumerable<Article>> GetMentioningArticlesAsync(IEnumerable<string> entityIds, DateOnly from, DateOnly to);
        (IEnumerable<Article> Items, int TotalCount) GetMentioningArticlesPaginatedAsync(string entityId, DateOnly from, DateOnly to, int page, int pageSize);
        IEnumerable<(string Domain, int Count)> GetMentioningArticlesByDomainAsync(string entityId, DateOnly from, DateOnly to);
        Entity? GetEntityByIdAsync(string entityId);
        IEnumerable<Entity> SearchEntitiesAsync(string query);
        IEnumerable<Entity> GetEntitiesPaginatedAsync(DateOnly from, DateOnly to, int page, int pageSize);
        Task<IEnumerable<Entity>> GetAllEntitiesAsync(DateOnly from, DateOnly to);
    }
}
