namespace Meridian.Api.Shared.Jobs;

public class WorkerOptions
{
    public const string SectionName = "Worker";

    public int Concurrency { get; set; } = 2;
    public int PollIntervalMs { get; set; } = 2000;
    public int HeartbeatIntervalMs { get; set; } = 30_000;
}
