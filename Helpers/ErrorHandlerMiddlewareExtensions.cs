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
            // Create an instance of the middleware to allow configuration before it's added to the pipeline
            var middlewareInstance = builder.ApplicationServices.GetService<ErrorHandlerMiddleware>();
            
            if (middlewareInstance == null)
            {
                // If not registered in DI, create a new instance
                middlewareInstance = new ErrorHandlerMiddleware(
                    builder.ApplicationServices.GetRequiredService<ILogger<ErrorHandlerMiddleware>>(),
                    builder.ApplicationServices.GetRequiredService<IHostEnvironment>());
            }

            configureOptions(middlewareInstance);
            return builder.UseMiddleware<ErrorHandlerMiddleware>();
        }
    }
}