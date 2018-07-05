﻿using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Linq;
using Toggl.Foundation.Analytics;
using Toggl.Foundation.Models;
using Toggl.Multivac;
using Toggl.Multivac.Extensions;
using Toggl.PrimeRadiant;
using Toggl.PrimeRadiant.Models;
using Toggl.Foundation.DTOs;
using Toggl.Foundation.Exceptions;
using Toggl.Foundation.Models.Interfaces;
using Toggl.Foundation.Sync.ConflictResolution;
using Toggl.Foundation.Extensions;

namespace Toggl.Foundation.DataSources
{
    internal sealed class TimeEntriesDataSource
        : ObservableDataSource<IThreadSafeTimeEntry, IDatabaseTimeEntry>,
          ITimeEntriesSource
    {
        private long? currentlyRunningTimeEntryId;

        private readonly ITimeService timeService;

        private readonly IRepository<IDatabaseTimeEntry> repository;

        private readonly IAnalyticsService analyticsService;

        private readonly Func<IDatabaseTimeEntry, IDatabaseTimeEntry, ConflictResolutionMode> alwaysCreate
            = (a, b) => ConflictResolutionMode.Create;

        public IObservable<bool> IsEmpty { get; }

        public IObservable<IThreadSafeTimeEntry> CurrentlyRunningTimeEntry { get; }

        protected override IRivalsResolver<IDatabaseTimeEntry> RivalsResolver { get; }

        public TimeEntriesDataSource(
            IRepository<IDatabaseTimeEntry> repository,
            ITimeService timeService,
            IAnalyticsService analyticsService)
            : base(repository)
        {
            Ensure.Argument.IsNotNull(timeService, nameof(timeService));
            Ensure.Argument.IsNotNull(repository, nameof(repository));
            Ensure.Argument.IsNotNull(analyticsService, nameof(analyticsService));

            this.timeService = timeService;
            this.repository = repository;
            this.analyticsService = analyticsService;

            RivalsResolver = new TimeEntryRivalsResolver(timeService);

            CurrentlyRunningTimeEntry =
                getCurrentlyRunningTimeEntry()
                    .StartWith()
                    .Merge(Created.Where(te => te.IsRunning()))
                    .Merge(Updated.Where(update => update.Id == currentlyRunningTimeEntryId).Select(update => update.Entity))
                    .Merge(Deleted.Where(id => id == currentlyRunningTimeEntryId).Select(_ => null as IThreadSafeTimeEntry))
                    .Select(runningTimeEntry)
                    .ConnectedReplay();

            IsEmpty =
                Observable.Return(default(IThreadSafeTimeEntry))
                    .StartWith()
                    .Merge(Updated.Select(tuple => tuple.Entity))
                    .Merge(Created)
                    .SelectMany(_ => GetAll(te => te.IsDeleted == false))
                    .Select(timeEntries => !timeEntries.Any());
        }

        public override IObservable<IThreadSafeTimeEntry> Create(IThreadSafeTimeEntry entity)
            => repository.BatchUpdate(new[] { (entity.Id, (IDatabaseTimeEntry)entity) }, alwaysCreate, RivalsResolver)
                .ToThreadSafeResult(Convert)
                .SelectMany(CommonFunctions.Identity)
                .Do(HandleConflictResolutionResult)
                .OfType<CreateResult<IThreadSafeTimeEntry>>()
                .FirstAsync()
                .Select(result => result.Entity);

        public IObservable<IThreadSafeTimeEntry> Stop(DateTimeOffset stopTime)
            => GetAll(te => te.IsDeleted == false && te.Duration == null)
                .Select(timeEntries => timeEntries.SingleOrDefault() ?? throw new NoRunningTimeEntryException())
                .SelectMany(timeEntry => timeEntry
                    .With((long)(stopTime - timeEntry.Start).TotalSeconds)
                    .Apply(Update));

        public IObservable<Unit> SoftDelete(IThreadSafeTimeEntry timeEntry)
            => Observable.Return(timeEntry)
                .Select(TimeEntry.DirtyDeleted)
                .SelectMany(repository.Update)
                .Do(entity => DeletedSubject.OnNext(entity.Id))
                .Select(_ => Unit.Default);

        public IObservable<IThreadSafeTimeEntry> Update(EditTimeEntryDto dto)
            => GetById(dto.Id)
                 .Select(te => createUpdatedTimeEntry(te, dto))
                 .SelectMany(Update);

        protected override IThreadSafeTimeEntry Convert(IDatabaseTimeEntry entity)
            => TimeEntry.From(entity);

        protected override ConflictResolutionMode ResolveConflicts(IDatabaseTimeEntry first, IDatabaseTimeEntry second)
            => Resolver.ForTimeEntries.Resolve(first, second);

        private TimeEntry createUpdatedTimeEntry(IThreadSafeTimeEntry timeEntry, EditTimeEntryDto dto)
            => TimeEntry.Builder.Create(dto.Id)
                        .SetDescription(dto.Description)
                        .SetDuration(dto.StopTime.HasValue ? (long?)(dto.StopTime.Value - dto.StartTime).TotalSeconds : null)
                        .SetTagIds(dto.TagIds)
                        .SetStart(dto.StartTime)
                        .SetTaskId(dto.TaskId)
                        .SetBillable(dto.Billable)
                        .SetProjectId(dto.ProjectId)
                        .SetWorkspaceId(dto.WorkspaceId)
                        .SetUserId(timeEntry.UserId)
                        .SetIsDeleted(timeEntry.IsDeleted)
                        .SetServerDeletedAt(timeEntry.ServerDeletedAt)
                        .SetAt(timeService.CurrentDateTime)
                        .SetSyncStatus(SyncStatus.SyncNeeded)
                        .Build();

        private IThreadSafeTimeEntry runningTimeEntry(IThreadSafeTimeEntry timeEntry)
        {
            timeEntry = timeEntry != null && timeEntry.IsRunning() ? timeEntry : null;
            currentlyRunningTimeEntryId = timeEntry?.Id;
            return timeEntry;
        }

        private IObservable<IThreadSafeTimeEntry> getCurrentlyRunningTimeEntry()
            => stopMultipleRunningTimeEntries()
                .SelectMany(_ => getAllRunning())
                .SelectMany(CommonFunctions.Identity)
                .SingleOrDefaultAsync();

        private IObservable<Unit> stopMultipleRunningTimeEntries()
            => getAllRunning()
                .Where(list => list.Count() > 1)
                .SelectMany(BatchUpdate)
                .Track(analyticsService.TwoRunningTimeEntriesInconsistencyFixed)
                .ToList()
                .SelectUnit()
                .DefaultIfEmpty(Unit.Default);

        private IObservable<IEnumerable<IThreadSafeTimeEntry>> getAllRunning()
            => GetAll(te => te.IsDeleted == false && te.Duration == null);
    }
}
