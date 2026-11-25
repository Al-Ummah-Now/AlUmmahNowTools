namespace AlUmmahNow.ToolsCore;

/// <summary>
/// Reports where the work is, as it reports. <see cref="Progress{T}"/> hands each report to the thread pool, so one could arrive
/// after a later one, or after the step it belongs to had ended.
/// </summary>
internal sealed class DirectProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}
