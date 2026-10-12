using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Time;

public static class SharedTimeExtensions
{
    /// <summary>
    /// Registers the application clock. Pass a custom <see cref="TimeProvider"/>
    /// in tests to freeze or advance time.
    /// </summary>
    public static IServiceCollection AddSharedTime(this IServiceCollection services, TimeProvider? custom = null)
    {
        if (custom is null)
            services.TryAddSingleton<IAppTimeProvider>(SystemAppTimeProvider.System);
        else
            services.AddSingleton<IAppTimeProvider>(new SystemAppTimeProvider(custom));
        return services;
    }
}
