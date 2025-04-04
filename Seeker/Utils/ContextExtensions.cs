using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.Widget;

namespace Seeker.Utils;

public static class ContextExtensions
{
    public static void ShowSimpleAlertDialog(this Context c, int messageResourceString, int actionResourceString)
    {
        new AndroidX.AppCompat.App.AlertDialog.Builder(c, ResourceConstant.Style.MyAlertDialogTheme)
            .SetMessage(messageResourceString)
            ?.SetPositiveButton(actionResourceString, OnCloseClick)
            .Create()
            .Show();
        return;

        void OnCloseClick(object sender, DialogClickEventArgs e)
        {
            (sender as AndroidX.AppCompat.App.AlertDialog)?.Dismiss();
        }
    }

    public static void ShowShortToast(this Context context, string message)
    {
        ShowToast(context, message, ToastLength.Short);
    }

    public static void ShowShortToast(this Context context, int stringResource)
    {
        ShowToast(context, stringResource, ToastLength.Short);
    }

    public static void ShowLongToast(this Context context, string message)
    {
        ShowToast(context, message, ToastLength.Long);
    }

    public static void ShowLongToast(this Context context, int stringResource)
    {
        ShowToast(context, stringResource, ToastLength.Long);
    }
    
    private static void ShowToast(this Context context, string message, ToastLength length)
    {
        if (context is Activity activity && ThreadingUtils.OnUiThread())
        {
            activity.RunOnUiThread(() => Toast.MakeText(activity, message, length)?.Show());
            return;
        }

        Toast.MakeText(context, message, length)?.Show();
    }

    private static void ShowToast(this Context context, int stringResource, ToastLength length)
    {
        ShowToast(context, context.GetString(stringResource), length);
    }
}
