namespace Meridian.Api.Features.Votes;

public static class VotesFeatureExtensions
{
    public static IServiceCollection AddVotesFeature(this IServiceCollection services)
    {
        services.AddScoped<GetVotes.Handler>();
        services.AddScoped<GetVote.Handler>();
        services.AddScoped<GetVoteTimeline.Handler>();
        services.AddScoped<SetPosition.Handler>();
        services.AddScoped<SetPositionsBulk.Handler>();

        return services;
    }
}
