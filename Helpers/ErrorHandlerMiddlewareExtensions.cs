using MyTts.Middleware;

namespace MyTts.Extensions
{
    public static class ErrorHandlerMiddlewareExtensions
    {
        /// <summary>
        /// Adds the custom ErrorHandlerMiddleware to the application's request pipeline.
        /// This middleware should be registered early in the pipeline to catch exceptions
        /// from subsequent middleware and controllers.
        /// </summary>
        /// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
        /// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
        public static IApplicationBuilder UseErrorHandlerMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ErrorHandlerMiddleware>();
        }

        /// <summary>
        /// Adds the custom ErrorHandlerMiddleware to the application's request pipeline
        /// with an option to configure custom exception-to-status-code mappings.
        /// </summary>
        /// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
        /// <param name="configureOptions">An action to configure custom exception mappings.</param>
        /// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
        public static IApplicationBuilder UseErrorHandlerMiddleware(this IApplicationBuilder builder, Action<ErrorHandlerMiddleware> configureOptions)
        {
            // Create an instance of the middleware to allow configuration before it's added to the pipeline.
            // This is a bit of a trick as middleware is usually instantiated by DI,
            // but for configuration, we can manually create it and then use its type.
            var middlewareInstance = (ErrorHandlerMiddleware)builder.ApplicationServices.GetService(typeof(ErrorHandlerMiddleware))
                                     ?? Activator.CreateInstance(typeof(ErrorHandlerMiddleware), builder.ApplicationServices.GetService<RequestDelegate>(),
                                                                builder.ApplicationServices.GetService<ILogger<ErrorHandlerMiddleware>>(),
                                                                builder.ApplicationServices.GetService<IHostEnvironment>()) as ErrorHandlerMiddleware;

            if (middlewareInstance == null)
            {
                throw new InvalidOperationException("Could not create an instance of ErrorHandlerMiddleware for configuration. Ensure it's registered with DI if you're using this overload.");
            }

            configureOptions(middlewareInstance);
            return builder.UseMiddleware<ErrorHandlerMiddleware>();
        }
    }
}