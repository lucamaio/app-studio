using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.App.Features.Projects;
using StudySpace.App.Features.Settings;
using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Projects;

namespace StudySpace.UnitTests;

public class PresentationTests
{
    [Fact]
    public async Task Empty_state_is_shown_only_after_successful_load()
    {
        var repository = new Repository();
        var service = new ProjectService(repository, new Clock());
        var navigation = new Navigation();
        var vm = new ProjectsViewModel(service, navigation, new ProjectActions(service, navigation, new TestDialogs()),
            NullLogger<ProjectsViewModel>.Instance);
        Assert.False(vm.IsEmpty);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
        repository.FailReads = true;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.IsEmpty);
        Assert.True(vm.HasError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Retrying_navigation_after_save_does_not_duplicate_the_project()
    {
        var repository = new Repository();
        var navigation = new Navigation { FailBack = true };
        var vm = new CreateProjectViewModel(new ProjectService(repository, new Clock()), navigation,
            NullLogger<CreateProjectViewModel>.Instance) { Name = "Reti" };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaved);
        Assert.True(vm.HasError);
        Assert.Single(repository.Items);
        navigation.FailBack = false;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Single(repository.Items);
        Assert.False(vm.HasError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Invalid_form_stays_on_page_and_shows_validation_message()
    {
        var navigation = new Navigation();
        var repository = new Repository();
        var vm = new CreateProjectViewModel(new ProjectService(repository, new Clock()), navigation,
            NullLogger<CreateProjectViewModel>.Instance) { Name = " " };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(repository.Items);
        Assert.False(vm.IsSaved);
        Assert.True(vm.HasError);
        Assert.Equal(0, navigation.BackCalls);
    }

    [Fact]
    public void Theme_selection_applies_and_failed_persistence_restores_previous_selection()
    {
        var themes = new Themes();
        var vm = new SettingsViewModel(themes, NullLogger<SettingsViewModel>.Instance);
        Assert.Equal(ThemePreference.System, vm.SelectedTheme!.Value);
        vm.SelectedTheme = vm.Themes[2];
        Assert.Equal(ThemePreference.Dark, themes.Current);
        themes.Fail = true;
        vm.SelectedTheme = vm.Themes[1];
        Assert.Equal(ThemePreference.Dark, vm.SelectedTheme!.Value);
        Assert.True(vm.HasError);
        themes.Fail = false;
        vm.SelectedTheme = vm.Themes[0];
        Assert.Equal(ThemePreference.System, themes.Current);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task Evaluation_settings_require_explicit_save_and_invalid_changes_preserve_working_config()
    {
        var store=new StudySpace.Application.Quizzes.MemorySemanticEvaluationSettingsStore();
        var vm=new SettingsViewModel(new Themes(),NullLogger<SettingsViewModel>.Instance,store);
        Assert.False(vm.EvaluationEnabled);vm.EvaluationEnabled=true;vm.EvaluationModel="local-model";
        Assert.False(store.Read().Enabled);await vm.SaveEvaluationCommand.ExecuteAsync(null);
        Assert.True(store.Read().Enabled);Assert.NotEmpty(vm.EvaluationSaved);
        vm.EvaluationEndpoint="file:///bad";Assert.Empty(vm.EvaluationSaved);
        await vm.SaveEvaluationCommand.ExecuteAsync(null);Assert.True(vm.HasError);
        Assert.Equal("http://localhost:11434",store.Read().Endpoint);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Repository : TestProjectRepository { }
    private sealed class Navigation : TestNavigation { }
    private sealed class Themes : IThemeService
    {
        public ThemePreference Current { get; private set; } = ThemePreference.System;
        public bool Fail { get; set; }
        public void Initialize() { }
        public void SetTheme(ThemePreference preference)
        {
            if (Fail) throw new IOException();
            Current = preference;
        }
    }
}



