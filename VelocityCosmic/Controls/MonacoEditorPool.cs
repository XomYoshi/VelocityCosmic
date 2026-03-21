using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

#nullable enable
namespace VelocityCosmic.Controls;

public static class MonacoEditorPool
{
    private static readonly
#nullable disable
    Queue<WebViewAPI> _pool = new Queue<WebViewAPI>();
    private static readonly object _lock = new object();
    private const int PreloadCount = 1;

    public static async Task InitializePool()
    {
        for (int i = 0; i < 1; ++i)
            await MonacoEditorPool.AddNewInstance();
    }

    public static WebViewAPI GetEditor()
    {
        lock (MonacoEditorPool._lock)
        {
            if (MonacoEditorPool._pool.Count > 0)
                return MonacoEditorPool._pool.Dequeue();
        }
        WebViewAPI editorInstance = MonacoEditorPool.CreateEditorInstance();
        MonacoEditorPool.PreloadOneAsync();
        return editorInstance;
    }

    public static async Task PreloadOneAsync() => await MonacoEditorPool.AddNewInstance();

    private static async Task AddNewInstance()
    {
        WebViewAPI editor = MonacoEditorPool.CreateEditorInstance();
        await MonacoEditorPool.WaitUntilEditorReady(editor);
        lock (MonacoEditorPool._lock)
            MonacoEditorPool._pool.Enqueue(editor);
        editor = (WebViewAPI)null;
    }

    private static WebViewAPI CreateEditorInstance()
    {
        WebViewAPI editorInstance = new WebViewAPI();
        ((FrameworkElement)editorInstance).HorizontalAlignment = HorizontalAlignment.Stretch;
        ((FrameworkElement)editorInstance).VerticalAlignment = VerticalAlignment.Stretch;
        editorInstance.Source = new Uri("http://localhost:3000/");
        ((UIElement)editorInstance).Visibility = Visibility.Hidden;
        return editorInstance;
    }

    public static void ReturnToPool(WebViewAPI editor)
    {
        if (editor == null)
            return;
        lock (MonacoEditorPool._lock)
            MonacoEditorPool._pool.Enqueue(editor);
    }

    private static Task WaitUntilEditorReady(WebViewAPI editor)
    {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        EventHandler handler = (EventHandler)null;
        handler = (EventHandler)((sender, e) =>
        {
            editor.EditorReady -= handler;
            tcs.TrySetResult(true);
        });
        editor.EditorReady += handler;
        return (Task)tcs.Task;
    }
}

