using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using SkillHorizon.Hosting;

namespace SkillHorizon.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddSkillHorizonMessaging(this IServiceCollection services, SkillHorizonOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Redis))
            throw new InvalidOperationException("SkillHorizon:Redis is required.");

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var configuration = ConfigurationOptions.Parse(options.Redis);
            configuration.AbortOnConnectFail = true;
            return ConnectionMultiplexer.Connect(configuration);
        });
        services.AddSingleton<IMessageBus, RedisMessageBus>();
        services.AddSingleton<IOutbox, PostgresOutbox>();
        services.AddSingleton<IOutboxPublisher, PostgresOutboxPublisher>();
        services.AddSingleton<ReferenceDistributor>();
        return services;
    }
}
