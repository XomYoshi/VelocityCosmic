using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace VelocityCosmic.Controls
{
    public class WebViewAPI : UserControl
    {
        private WebView2? webView;
        private bool isReady;

        public event EventHandler? EditorReady;

        public Uri? Source
        {
            get => webView?.Source;
            set
            {
                if (webView != null)
                {
                    webView.Source = value;
                }
            }
        }

        public WebViewAPI()
        {
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                webView = new WebView2();
                this.Content = webView;

                await webView.EnsureCoreWebView2Async();

                webView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    if (!isReady)
                    {
                        isReady = true;
                        EditorReady?.Invoke(this, EventArgs.Empty);
                    }
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Fixed GetText method in WebViewAPI class
        public async Task<string> GetText()
        {
            if (webView?.CoreWebView2 == null)
                return string.Empty;

            try
            {
                var result = await webView.CoreWebView2.ExecuteScriptAsync("window.editor?.getValue() || ''");

                // Remove outer quotes that ExecuteScriptAsync adds
                if (result.StartsWith("\"") && result.EndsWith("\""))
                {
                    result = result.Substring(1, result.Length - 2);
                }

                // CRITICAL FIX: Properly unescape ALL JSON escape sequences
                result = System.Text.RegularExpressions.Regex.Unescape(result);

                // Alternative if Regex.Unescape doesn't work:
                // result = result.Replace("\\n", "\n")
                //                .Replace("\\r", "\r")
                //                .Replace("\\t", "\t")
                //                .Replace("\\\\", "\\")
                //                .Replace("\\\"", "\"")
                //                .Replace("\\'", "'");

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetText error: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task SetText(string text)
        {
            if (webView?.CoreWebView2 == null)
                return;

            try
            {
                // Escape the text for JavaScript
                var escapedText = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                await webView.CoreWebView2.ExecuteScriptAsync($"window.editor?.setValue(\"{escapedText}\")");
            }
            catch
            {
                // Ignore errors
            }
        }
    }
}
