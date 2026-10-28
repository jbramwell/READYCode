// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Threading;
using ReadyCode.Logging;

namespace ReadyCode;

/// <summary>
/// Interaction logic for App.xaml. Also installs the app-wide safety net for unexpected
/// exceptions: each one is written to <see cref="ErrorLog"/>, and an error on the UI thread -
/// which includes every <c>async void</c> event handler, since their exceptions are re-raised
/// on the dispatcher - is reported and recovered from rather than closing the app, so the user
/// keeps the chance to save unsaved work.
/// </summary>
public partial class App : Application
{
    #region Private Fields

    // Minimum gap between recovered-error dialogs. An exception thrown from a layout/render pass
    // recurs on every frame, so without this the user could be trapped behind an endless run of
    // dialogs instead of getting back to the editor to save their work. Errors in the gap are
    // still logged.
    private static readonly TimeSpan ErrorDialogCooldown = TimeSpan.FromSeconds(10);

    private bool _isShowingErrorDialog;
    private DateTime _lastErrorDialogClosedUtc = DateTime.MinValue;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Installs the global exception handlers before the main window (StartupUri) is created,
    /// so an error while it's being built is caught too.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);
    }

    #endregion

    #region Private Methods

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write(e.Exception, "UI thread");
        e.Handled = true;

        // Failed before the main window finished loading - there's no usable window to return
        // to, and keeping the process alive would just leave it running invisibly.
        if (MainWindow is not { IsLoaded: true })
        {
            MessageBox.Show(
                $"READYCode couldn't start because of an unexpected error.\n\n{e.Exception.Message}\n\nDetails were written to:\n{ErrorLog.LogFilePath}",
                "READYCode", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (_isShowingErrorDialog || DateTime.UtcNow - _lastErrorDialogClosedUtc < ErrorDialogCooldown)
            return;

        _isShowingErrorDialog = true;
        try
        {
            // Owned by whichever window is active (e.g. the Settings dialog, if the error came
            // from there) so the message appears in front of it rather than behind.
            var owner = Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? MainWindow;
            MessageBox.Show(owner,
                $"Something went wrong, but READYCode has recovered and is still running.\n\n{e.Exception.Message}\n\nSaving your work now is recommended. Details were written to:\n{ErrorLog.LogFilePath}",
                "READYCode", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isShowingErrorDialog = false;
            _lastErrorDialogClosedUtc = DateTime.UtcNow;
        }
    }

    // An exception on a background thread can't be recovered from - the runtime terminates the
    // process once this returns - so all that's left is to record it and tell the user where.
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString());
        ErrorLog.Write(exception, "Background thread (fatal)");

        MessageBox.Show(
            $"READYCode hit an unexpected error and has to close.\n\n{exception.Message}\n\nDetails were written to:\n{ErrorLog.LogFilePath}",
            "READYCode", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // A faulted Task nobody awaited doesn't crash the process in .NET, so its exception would
    // otherwise vanish silently - log it so it isn't lost.
    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ErrorLog.Write(e.Exception, "Unobserved task");
        e.SetObserved();
    }

    #endregion
}
