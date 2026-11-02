namespace Meridian.IntegrationTests.Infrastructure;

public static class Wait
{
    public static async Task Until(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(20);
        }
    }
}
