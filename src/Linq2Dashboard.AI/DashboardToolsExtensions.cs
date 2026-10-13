namespace Linq2Dashboard.AI;

/// <summary>Turns a dashboard into agent tools (design §10.10).</summary>
public static class DashboardToolsExtensions
{
    /// <summary>
    /// The dashboard as describe, query and, with <see cref="DashboardToolOptions{T}.Show"/>, show tools. Everything is
    /// checked here: the name, the descriptions, the filterable properties. Give the agent
    /// <see cref="DashboardTools{T}.Functions"/>.
    /// </summary>
    public static DashboardTools<T> ToTools<T>(this Dashboard<T> dashboard, DashboardToolOptions<T> options)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(options);
        return new DashboardTools<T>(dashboard, options);
    }
}
