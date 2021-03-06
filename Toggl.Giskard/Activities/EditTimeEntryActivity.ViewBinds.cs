﻿using Android.Views;
using Android.Widget;
using MvvmCross.Droid.Support.V7.AppCompat;
using Toggl.Foundation.MvvmCross.ViewModels;
using static Toggl.Giskard.Resource.Id;

namespace Toggl.Giskard.Activities
{
    public sealed partial class EditTimeEntryActivity : MvxAppCompatActivity<EditTimeEntryViewModel>
    {
        private View startTimeArea;
        private View stopTimeArea;
        private View durationArea;
        private View projectContainer;
        private TextView projectTaskClientTextView;

        private void initializeViews()
        {
            startTimeArea = FindViewById(EditTimeLeftPart);
            stopTimeArea = FindViewById(EditTimeRightPart);
            durationArea = FindViewById(EditDuration);
            projectContainer = FindViewById(EditTimeEntryProjectContainer);
            projectTaskClientTextView = FindViewById<TextView>(EditProjectTaskClient);
        }
    }
}
