using Inżynierka.Models;

namespace Inżynierka.Tests;

// Test startowy: potwierdza, że projekt testów widzi kod aplikacji i że `dotnet test` działa.
public class AttractionCategoryCatalogTests
{
    [Fact]
    public void All_HasNineCategoriesWithUniqueCodes()
    {
        var codes = AttractionCategoryCatalog.All.Select(c => c.Code).ToList();

        Assert.Equal(9, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }
}
