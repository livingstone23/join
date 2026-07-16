namespace JOIN.Application.Common;



/// <summary>
/// Configures the latency thresholds used by <see cref="PerformanceBehavior{TRequest,TResponse}"/>
/// to emit a warning when a MediatR request exceeds the allowed execution time.
/// </summary>
public class PerformanceSettings
{
    /// <summary>
    /// Threshold in milliseconds above which a request is considered long-running and a warning is logged.
    /// </summary>
    public int ThresholdMilliseconds { get; set; } = 200;
}