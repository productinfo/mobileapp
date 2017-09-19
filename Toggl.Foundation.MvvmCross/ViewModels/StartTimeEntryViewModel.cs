﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Toggl.Foundation.Autocomplete;
using Toggl.Foundation.Autocomplete.Suggestions;
using Toggl.Foundation.DataSources;
using Toggl.Foundation.DTOs;
using Toggl.Foundation.MvvmCross.Parameters;
using Toggl.Multivac;
using Toggl.Multivac.Extensions;

namespace Toggl.Foundation.MvvmCross.ViewModels
{
    [Preserve(AllMembers = true)]
    public sealed class StartTimeEntryViewModel : MvxViewModel<DateTimeOffset>
    {
        //Fields
        private readonly ITimeService timeService;
        private readonly ITogglDataSource dataSource;
        private readonly IMvxNavigationService navigationService;
        private readonly Subject<TextFieldInfo> infoSubject = new Subject<TextFieldInfo>();

        private IDisposable queryDisposable;
        private IDisposable elapsedTimeDisposable;

        //Properties
        public bool IsEditingDuration { get; private set; }

        public bool IsEditingStartDate { get; private set; }

        public bool IsSuggestingProjects { get; set; }

        public TextFieldInfo TextFieldInfo { get; set; } = new TextFieldInfo("", 0);

        public TimeSpan ElapsedTime { get; private set; } = TimeSpan.Zero;

        public bool IsBillable { get; private set; } = false;

        public bool IsEditingProjects { get; private set; } = false;

        public bool IsEditingTags { get; private set; } = false;

        public DateTimeOffset StartDate { get; private set; }

        public DateTimeOffset? EndDate { get; private set; }

        public MvxObservableCollection<AutocompleteSuggestion> Suggestions { get; }
            = new MvxObservableCollection<AutocompleteSuggestion>();

        public IMvxAsyncCommand BackCommand { get; }

        public IMvxAsyncCommand DoneCommand { get; }

        public IMvxAsyncCommand ChangeDurationCommand { get; }
        
        public IMvxAsyncCommand ChangeStartTimeCommand { get; }

        public IMvxCommand ToggleBillableCommand { get; }

        public IMvxCommand ToggleProjectSuggestionsCommand { get; }

        public IMvxCommand<AutocompleteSuggestion> SelectSuggestionCommand { get; }

        public StartTimeEntryViewModel(ITogglDataSource dataSource, ITimeService timeService, 
            IMvxNavigationService navigationService)
        {
            Ensure.Argument.IsNotNull(dataSource, nameof(dataSource));
            Ensure.Argument.IsNotNull(timeService, nameof(timeService));
            Ensure.Argument.IsNotNull(navigationService, nameof(navigationService));

            this.dataSource = dataSource;
            this.timeService = timeService;
            this.navigationService = navigationService;

            BackCommand = new MvxAsyncCommand(back);
            DoneCommand = new MvxAsyncCommand(done);
            ToggleBillableCommand = new MvxCommand(toggleBillable);
            ChangeDurationCommand = new MvxAsyncCommand(changeDuration);
            ChangeStartTimeCommand = new MvxAsyncCommand(changeStartTime);
            ToggleProjectSuggestionsCommand = new MvxCommand(toggleProjectSuggestions);
            SelectSuggestionCommand = new MvxCommand<AutocompleteSuggestion>(selectSuggestion);
        }

        private void selectSuggestion(AutocompleteSuggestion suggestion)
        {
            switch (suggestion)
            {
                case QuerySymbolSuggestion querySymbolSuggestion:
                    TextFieldInfo = new TextFieldInfo(querySymbolSuggestion.Symbol, 1);
                    break;

                case TimeEntrySuggestion timeEntrySuggestion:
                    
                    TextFieldInfo = new TextFieldInfo(
                        timeEntrySuggestion.Description,
                        timeEntrySuggestion.Description.Length,
                        timeEntrySuggestion.ProjectId,
                        timeEntrySuggestion.ProjectName,
                        timeEntrySuggestion.ProjectColor
                    );

                    break;

                case ProjectSuggestion projectSuggestion:
                    
                    TextFieldInfo = TextFieldInfo
                        .RemoveProjectQueryFromDescriptionIfNeeded()
                        .WithProjectInfo(
                            projectSuggestion.ProjectId,
                            projectSuggestion.ProjectName, 
                            projectSuggestion.ProjectColor
                        );
                    break;
            }
        }

        public override void Prepare(DateTimeOffset parameter)
        {
            StartDate = parameter;

            elapsedTimeDisposable =
                timeService.CurrentDateTimeObservable.Subscribe(currentTime => ElapsedTime = currentTime - StartDate);

            queryDisposable = 
                Observable.Return(TextFieldInfo).StartWith()
                    .Merge(infoSubject.AsObservable())
                    .SelectMany(dataSource.AutocompleteProvider.Query)
                    .Subscribe(onSuggestions);
        }

        private void OnTextFieldInfoChanged()
        {
            infoSubject.OnNext(TextFieldInfo);
        }

        private void toggleProjectSuggestions()
        {
            if (IsSuggestingProjects)
            {
                TextFieldInfo = TextFieldInfo.RemoveProjectQueryFromDescriptionIfNeeded();
                return;
            }

            if (TextFieldInfo.ProjectId != null)
            {
                infoSubject.OnNext(new TextFieldInfo(QuerySymbols.ProjectsString, 1));
                return;
            }

            var newText = TextFieldInfo.Text.Insert(TextFieldInfo.CursorPosition, QuerySymbols.ProjectsString);
            TextFieldInfo = new TextFieldInfo(newText, TextFieldInfo.CursorPosition + 1);
        }

        private void toggleBillable() => IsBillable = !IsBillable;

        private async Task changeStartTime()
        {
            IsEditingStartDate = true;

            var currentlySelectedDate = StartDate;
            StartDate = await navigationService
                .Navigate<SelectDateTimeViewModel, DateTimeOffset, DateTimeOffset>(currentlySelectedDate)
                .ConfigureAwait(false);

            IsEditingStartDate = false;
        }

        private Task back() => navigationService.Close(this);

        private async Task changeDuration()
        {
            IsEditingDuration = true;

            var currentDuration = DurationParameter.WithStartAndStop(StartDate, null);
            var selectedDuration = await navigationService
                .Navigate<EditDurationViewModel, DurationParameter, DurationParameter>(currentDuration)
                .ConfigureAwait(false);

            StartDate = selectedDuration.Start;

            IsEditingDuration = false;
        }

        private async Task done()
        {
            await dataSource.User.Current()
                .Select(user => new StartTimeEntryDTO
                {
                    UserId = user.Id,
                    StartTime = StartDate,
                    Billable = IsBillable,
                    Description = TextFieldInfo.Text,
                    ProjectId = TextFieldInfo.ProjectId,
                    WorkspaceId = user.DefaultWorkspaceId
                })
                .SelectMany(dataSource.TimeEntries.Start);

            await navigationService.Close(this);
        }

        private void onSuggestions(IEnumerable<AutocompleteSuggestion> suggestions)
        {
            IsSuggestingProjects = suggestions.FirstOrDefault() is ProjectSuggestion;

            Suggestions.Clear();
            Suggestions.AddRange(suggestions.Distinct(AutocompleteSuggestionComparer.Instance));
        }
    }
}
