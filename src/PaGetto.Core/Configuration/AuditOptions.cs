namespace PaGetto.Core.Configuration;

public class AuditOptions
{
    public const int DefaultRetentionDays = 30;

    /// <summary>
    /// How many days the audit page keeps its events. Older events are deleted once a day.
    /// 0 keeps them forever. Default is 30.
    /// </summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;
}
