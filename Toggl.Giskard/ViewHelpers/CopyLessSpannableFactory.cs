using Android.Text;
using Java.Lang;

namespace Toggl.Giskard.ViewHelpers
{
    public class CopylessSpannableFactory : SpannableFactory
    {
        public override ISpannable NewSpannable(ICharSequence source)
        {
            return source as ISpannable;
        }
    }
}
