using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Projects;

namespace StudySpace.UnitTests;

public class ProjectServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_name_is_rejected_without_writing(string name)
    {
        var repository = new MemoryRepository();
        var service = new ProjectService(repository, new FixedClock());
        await Assert.ThrowsAsync<StudyValidationException>(() => service.CreateAsync(name, null));
        Assert.Empty(repository.Projects);
    }

    [Fact]
    public async Task Name_and_description_limits_are_enforced()
    {
        var repository = new MemoryRepository();
        var service = new ProjectService(repository, new FixedClock());
        await Assert.ThrowsAsync<StudyValidationException>(() => service.CreateAsync(new string('x', 121), ""));
        await Assert.ThrowsAsync<StudyValidationException>(() => service.CreateAsync("Reti", new string('x', 2001)));
        Assert.Empty(repository.Projects);
    }

    [Fact]
    public async Task Create_normalizes_input_and_uses_the_clock()
    {
        var repository = new MemoryRepository();
        var clock = new FixedClock();
        var project = await new ProjectService(repository, clock).CreateAsync("  Reti  ", "  TCP/IP  ");
        Assert.Equal("Reti", project.Name);
        Assert.Equal("TCP/IP", project.Description);
        Assert.Equal(clock.UtcNow, project.CreatedAtUtc);
        Assert.Equal(project.CreatedAtUtc, project.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal(1, project.Revision);
        Assert.False(project.IsFavorite);
        Assert.Same(project, Assert.Single(repository.Projects));
    }

    [Fact]
    public async Task Cancelled_create_is_not_reported_as_success()
    {
        var repository = new MemoryRepository();
        var service = new ProjectService(repository, new FixedClock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CreateAsync("Reti", null, cancellation.Token));
        Assert.Empty(repository.Projects);
    }

    [Fact]
    public async Task Negative_page_offset_is_rejected()
    {
        var service = new ProjectService(new MemoryRepository(), new FixedClock());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListAsync(-1));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoryRepository : TestProjectRepository
    {
        public List<StudyProject> Projects => Items;
    }
}

