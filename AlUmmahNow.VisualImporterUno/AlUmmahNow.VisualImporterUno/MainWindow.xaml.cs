using AlUmmahNow.ToolsCore;
using AlUmmahNow.VisualImporterUno.Shell;
using Microsoft.UI.Xaml;

namespace AlUmmahNow.VisualImporterUno
{
    /// <summary>
    /// The app's window: hosts the shell (header, sign-in, staff gate, workspace). The navigation
    /// calls the pages make go to the workspace when it is shown.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly ShellState _state;
        private ToolsShell? _shell;

        public MainWindow(ShellState state)
        {
            _state = state;
            InitializeComponent();
            UpdateTitle();
            BuildShell();
            _state.LanguageChanged += (_, _) => DispatcherQueue.TryEnqueue(BuildShell);
            _state.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdateTitle);
            Closed += (_, _) => (_shell?.Workspace as IDisposable)?.Dispose();
        }

        public ToolsShell? Shell => _shell;

        private WorkspaceView? Workspace => _shell?.Workspace as WorkspaceView;

        /// <summary>A new shell in the new language: every x:Uid string and the flow direction reload</summary>
        private void BuildShell()
        {
            (_shell?.Workspace as IDisposable)?.Dispose();
            _shell = new ToolsShell(_state, () => new WorkspaceView());
            Host.Children.Clear();
            Host.Children.Add(_shell);
        }

        private void UpdateTitle()
        {
            var title = ToolsShell.L("Shell_WindowTitle");
            Title = _state.Session.Environment.Kind switch
            {
                ToolsEnvironmentKind.Qa => title + " (" + ToolsShell.L("Shell_BadgeQa") + ")",
                ToolsEnvironmentKind.Stage => title + " (" + ToolsShell.L("Shell_BadgeStage") + ")",
                _ => title,
            };
        }

        public void NavigateToImportView() => Workspace?.NavigateToImportView();
        public void NavigateToDashboardView() => Workspace?.NavigateToDashboardView();
        public void NavigateToSettingsView() => Workspace?.NavigateToSettingsView();
        public void NavigateToWatchManagementView() => Workspace?.NavigateToWatchManagementView();
        public void NavigateToApprovalWorkflowView() => Workspace?.NavigateToApprovalWorkflowView();
        public void NavigateToAnalyticsView() => Workspace?.NavigateToAnalyticsView();

        public void UpdateScanProgress(double progressPercentage, string currentOperation, int processedFiles, int totalFiles, int filesFound) =>
            Workspace?.UpdateScanProgress(progressPercentage, currentOperation, processedFiles, totalFiles, filesFound);

        public void UpdateOperationProgress(double globalProgress, double localProgress, string operation, int processedFiles, int totalFiles, string currentItem) =>
            Workspace?.UpdateOperationProgress(globalProgress, localProgress, operation, processedFiles, totalFiles, currentItem);
    }
}
