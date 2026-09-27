namespace PaGetto.Core.Configuration;

public class RequestRateLimitOptions
{
    /// <summary>
    /// Limits the number of requests per client with a fixed window. Authenticated requests are
    /// partitioned by user name, anonymous requests by client IP address. Default is false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The maximum number of requests a client can make in one window. Default is 600.
    /// </summary>
    public int PermitLimit { get; set; } = 600;

    /// <summary>
    /// The length of the window in seconds. Default is 60.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// The number of requests over the limit that wait for the next window instead of
    /// being rejected with 429. Default is 0.
    /// </summary>
    public int QueueLimit { get; set; }
}
