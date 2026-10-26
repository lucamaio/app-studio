using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.App.Features.Projects;
using StudySpace.App.Services;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
namespace StudySpace.UnitTests;

public class ProjectManagementTests
{
    [Fact]
    public async Task Tags_are_trimmed_deduplicated_and_optional_icon_is_supported()
    {
        var repo = new TestProjectRepository();
        var p = await new ProjectService(repo, new TestClock()).CreateAsync(
            new(" Reti ", " TCP/IP ", null, "#167b9b", true, [" Esame ", "esame", "", "Informatica"]));
        Assert.Equal(["Esame", "Informatica"], p.Tags);
        Assert.Equal("", p.IconKey);
        Assert.Equal("#167B9B", p.Color);
        Assert.True(p.IsFavorite);
    }
    [Theory]
    [InlineData("red", "book")]
    [InlineData("#xyzxyz", "book")]
    [InlineData("#123456", "unsupported")]
    public async Task Invalid_appearance_is_rejected(string color, string icon)
    {
        var repo = new TestProjectRepository();
        await Assert.ThrowsAsync<StudyValidationException>(() => new ProjectService(repo, new TestClock())
            .CreateAsync(new("Reti", Color: color, IconKey: icon)));
        Assert.Empty(repo.Items);
    }
    [Fact]
    public async Task Tag_limits_are_enforced()
    {
        var service = new ProjectService(new TestProjectRepository(), new TestClock());
        await Assert.ThrowsAsync<StudyValidationException>(() => service.CreateAsync(new("Reti", Tags: [new string('x', 33)])));
        await Assert.ThrowsAsync<StudyValidationException>(() => service.CreateAsync(new("Reti", Tags: Enumerable.Range(0, 11).Select(i => $"tag{i}").ToArray())));
    }
    [Fact]
    public async Task Update_preserves_identity_and_creation_and_advances_revision()
    {
        var repo = new TestProjectRepository(); var clock = new TestClock(); var service = new ProjectService(repo, clock);
        var p = await service.CreateAsync("Reti", null);
        clock.UtcNow = clock.UtcNow.AddDays(1);
        await service.UpdateAsync(p.Id, p.Revision, new("Architettura", Tags: ["Esame"]));
        var changed = await service.GetAsync(p.Id);
        Assert.Equal(p.CreatedAtUtc, changed.CreatedAtUtc);
        Assert.Equal(clock.UtcNow, changed.UpdatedAtUtc);
        Assert.Equal(2, changed.Revision);
        await Assert.ThrowsAsync<ProjectConflictException>(() => service.UpdateAsync(p.Id, 1, new("Obsoleto")));
        Assert.Equal("Architettura", (await service.GetAsync(p.Id)).Name);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_requires_positive_confirmation(bool confirm)
    {
        var repo = new TestProjectRepository(); var service = new ProjectService(repo, new TestClock());
        var p = await service.CreateAsync("Sicurezza", null);
        var dialogs = new TestDialogs { Confirmed = confirm };
        var result = await new ProjectActions(service, new TestNavigation(), dialogs).ExecuteAsync(p, ProjectMenuAction.Delete);
        Assert.Equal(1, dialogs.ConfirmCalls);
        Assert.Equal("Sicurezza", dialogs.ConfirmedName);
        Assert.Equal(confirm ? 1 : 0, repo.DeleteCalls);
        Assert.Equal(confirm ? ProjectActionResult.Deleted : ProjectActionResult.None, result);
        Assert.Equal(confirm ? 0 : 1, repo.Items.Count);
    }
    [Fact]
    public async Task Favorites_page_cannot_leak_nonfavorites_and_refreshes_after_removal()
    {
        var repo = new TestProjectRepository(); var service = new ProjectService(repo, new TestClock());
        await service.CreateAsync(new("A", IsFavorite: true));
        await service.CreateAsync(new("B"));
        var nav = new TestNavigation();
        var vm = new ProjectsViewModel(service, nav, new(service, nav, new TestDialogs()), NullLogger<ProjectsViewModel>.Instance);
        vm.ConfigureFavorites();
        vm.FavoritesOnly = false; // Page mode still enforces the filter.
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal("A", Assert.Single(vm.Projects).Name);
        await vm.Projects[0].FavoriteCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Projects);
    }
    [Fact]
    public async Task Edit_form_loads_saved_fields_and_saves_once()
    {
        var repo = new TestProjectRepository(); var service = new ProjectService(repo, new TestClock());
        var p = await service.CreateAsync(new("Reti", "Appunti", "network", "#167B9B", true, ["Esame"]));
        var nav = new TestNavigation { FailBack = true };
        var vm = new CreateProjectViewModel(service, nav, NullLogger<CreateProjectViewModel>.Instance);
        vm.Configure(p.Id.ToString());
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditing);
        Assert.Equal("Esame", vm.TagsText);
        Assert.Equal("network", vm.SelectedIcon!.Key);
        vm.Name = "Reti avanzate";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaved);
        nav.FailBack = false;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, Assert.Single(repo.Items).Revision);
        Assert.Equal("Reti avanzate", repo.Items[0].Name);
    }
    [Fact]
    public async Task Invalid_edit_route_cannot_create_a_project()
    {
        var repo = new TestProjectRepository(); var service = new ProjectService(repo, new TestClock());
        var vm = new CreateProjectViewModel(service, new TestNavigation(), NullLogger<CreateProjectViewModel>.Instance);
        vm.Configure("invalid");
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsReady);
        Assert.True(vm.HasError);
        Assert.Empty(repo.Items);
    }
    [Fact]
    public async Task Dashboard_scopes_sections_and_delete_returns_to_projects()
    {
        var repo = new TestProjectRepository(); var service = new ProjectService(repo, new TestClock());
        var p = await service.CreateAsync("Reti", null); var nav = new TestNavigation();
        var dialogs = new TestDialogs { Confirmed = true };
        var vm = new ProjectDashboardViewModel(service, nav, new(service, nav, dialogs), NullLogger<ProjectDashboardViewModel>.Instance);
        vm.Configure(p.Id.ToString());
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenSectionCommand.ExecuteAsync("sources");
        Assert.Equal(p.Id, nav.OpenedProject);
        Assert.Equal("sources", nav.Section);
        await vm.Card!.DeleteCommand.ExecuteAsync(null);
        Assert.True(nav.AtProjects);
        Assert.False(vm.HasProject);
    }
}

