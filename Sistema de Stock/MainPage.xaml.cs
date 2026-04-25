using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.AspNetCore.Components.WebView;


#if ANDROID
using Android.Webkit;
#endif

namespace Sistema_de_Stock
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private void OnBlazorWebViewInitializing(object sender, BlazorWebViewInitializingEventArgs e)
        {
#if ANDROID
            e.WebView.SetWebChromeClient(new PermissionWebChromeClient());
#endif

#if WINDOWS
            e.UserDataFolder = Path.Combine(FileSystem.CacheDirectory, "WebView2");
#endif
        }

        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();
#if WINDOWS
            if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView2)
            {
                webView2.CoreWebView2Initialized += (s, e) =>
                {
                    webView2.CoreWebView2.PermissionRequested += (sender, args) =>
                    {
                        if (args.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Camera)
                        {
                            args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
                        }
                    };
                };
            }
#endif
        }
    }

#if ANDROID
    internal class PermissionWebChromeClient : WebChromeClient
    {
        public override void OnPermissionRequest(PermissionRequest request)
        {
            request.Grant(request.GetResources());
        }
    }
#endif
}
