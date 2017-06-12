﻿using System;
using Realms;
using Toggl.PrimeRadiant.Models;

namespace Toggl.PrimeRadiant.Realm
{
    internal partial class RealmProject : RealmObject, IDatabaseProject
    {
        public int WorkspaceId { get; set; }

        public int? ClientId { get; set; }

        public string Name { get; set; }

        public bool IsPrivate { get; set; }

        public bool Active { get; set; }

        public DateTimeOffset At { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? ServerDeletedAt { get; set; }

        public string Color { get; set; }

        public bool Billable { get; set; }

        public bool Template { get; set; }

        public bool AutoEstimates { get; set; }

        public int? EstimatedHours { get; set; }

        public int? Rate { get; set; }

        public string Currency { get; set; }

        public int ActualHours { get; set; }
    }
}
