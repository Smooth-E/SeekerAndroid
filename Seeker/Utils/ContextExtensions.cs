using _Microsoft.Android.Resource.Designer;
using Android.Content;

namespace Seeker.Utils;

public static class ContextExtensions
{
    public static void ShowSimpleAlertDialog(this Context c, int messageResourceString, int actionResourceString)
    {

        void OnCloseClick(object sender, DialogClickEventArgs e)
        {
            (sender as AndroidX.AppCompat.App.AlertDialog)?.Dismiss();
        }

        new AndroidX.AppCompat.App.AlertDialog.Builder(c, ResourceConstant.Style.MyAlertDialogTheme)
            .SetMessage(messageResourceString)
            ?.SetPositiveButton(actionResourceString, OnCloseClick)
            .Create()
            .Show();
    }
}
