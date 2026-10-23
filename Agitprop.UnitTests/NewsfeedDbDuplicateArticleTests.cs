using Agitprop.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Agitprop.UnitTests;

public class NewsfeedDbDuplicateArticleTests
{
    [Test]
    public void IsArticleUrlUniqueViolation_MatchesOnlyArticleUrlConstraint()
    {
        var articleUrlConflict = new DbUpdateException(
            "Save failed",
            new PostgresException("duplicate", "ERROR", "ERROR", "23505", constraintName: "IX_articles_Url"));
        var otherUniqueConflict = new DbUpdateException(
            "Save failed",
            new PostgresException("duplicate", "ERROR", "ERROR", "23505", constraintName: "IX_entities_Name"));
        var nonUniqueFailure = new DbUpdateException(
            "Save failed",
            new PostgresException("unavailable", "FATAL", "FATAL", "08006"));

        Assert.Multiple(() =>
        {
            Assert.That(NewsfeedDB.IsArticleUrlUniqueViolation(articleUrlConflict), Is.True);
            Assert.That(NewsfeedDB.IsArticleUrlUniqueViolation(otherUniqueConflict), Is.False);
            Assert.That(NewsfeedDB.IsArticleUrlUniqueViolation(nonUniqueFailure), Is.False);
        });
    }
}
