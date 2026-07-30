using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Repository-class-specific behavior — distinct from CatalogItemPersistenceTests (Slice 1),
/// which proves raw DbContext round-tripping. Same AddAsync/GetByIdAsync contract shape as
/// LeadRepositoryTests, plus one test specific to BR-14/D38: GetByIdAsync must still return a
/// retired item, not filter it out.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class CatalogItemRepositoryTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task AddAsync_FollowedBySaveChangesAsync_PersistsTheCatalogItem()
    {
        await using var context = fixture.CreateContext();
        var repository = new CatalogItemRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var catalogItem = CatalogItem.Create("Bodenbelag verlegen", ItemUnit.SquareMeter(), Money.FromExact(25.00m));

        await repository.AddAsync(catalogItem, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.CatalogItems.SingleOrDefaultAsync(c => c.Id == catalogItem.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Bodenbelag verlegen", reloaded.Title);
    }

    [Fact]
    public async Task AddAsync_WithoutSaveChangesAsync_PersistsNothing()
    {
        await using (var context = fixture.CreateContext())
        {
            var repository = new CatalogItemRepository(context);
            var catalogItem = CatalogItem.Create("Never Saved", ItemUnit.Piece(), Money.FromExact(1.00m));

            await repository.AddAsync(catalogItem, CancellationToken.None);
            // Deliberately no call to IUnitOfWork.SaveChangesAsync here.
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.CatalogItems.SingleOrDefaultAsync(c => c.Title == "Never Saved");
        Assert.Null(reloaded);
    }

    [Fact]
    public async Task GetByIdAsync_AfterAddingViaADifferentContextInstance_ReturnsThePersistedCatalogItem()
    {
        var catalogItem = CatalogItem.Create("Fliesen verlegen", ItemUnit.SquareMeter(), Money.FromExact(30.00m), "Feinsteinzeug");

        await using (var writeContext = fixture.CreateContext())
        {
            var writeRepository = new CatalogItemRepository(writeContext);
            var writeUnitOfWork = new UnitOfWork(writeContext);
            await writeRepository.AddAsync(catalogItem, CancellationToken.None);
            await writeUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        var readRepository = new CatalogItemRepository(readContext);
        var reloaded = await readRepository.GetByIdAsync(catalogItem.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal("Fliesen verlegen", reloaded.Title);
        Assert.Equal("Feinsteinzeug", reloaded.DefaultSpecification);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCatalogItemDoesNotExist_ReturnsNull()
    {
        await using var context = fixture.CreateContext();
        var repository = new CatalogItemRepository(context);

        var result = await repository.GetByIdAsync(-1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ForARetiredCatalogItem_StillReturnsIt()
    {
        var catalogItem = CatalogItem.Create("Retired Item", ItemUnit.Piece(), Money.FromExact(5.00m));
        catalogItem.Retire();

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.CatalogItems.Add(catalogItem);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var repository = new CatalogItemRepository(readContext);

        var reloaded = await repository.GetByIdAsync(catalogItem.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.True(reloaded.IsRetired);
    }
}
