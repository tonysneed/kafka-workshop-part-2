using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Worker.Options;

namespace Worker
{
    class Program
    {
        private static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // Add options
                    var consumerOptions = hostContext.Configuration
                        .GetSection(nameof(ConsumerOptions))
                        .Get<ConsumerOptions>();
                    services.AddSingleton(consumerOptions);
                    var producerOptions = hostContext.Configuration
                        .GetSection(nameof(ProducerOptions))
                        .Get<ProducerOptions>();
                    services.AddSingleton(producerOptions);

                    // Add logger
                    services.AddSingleton<ILogger>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<KafkaWorker>();
                        logger.LogInformation($"Hosting Environment: {hostContext.HostingEnvironment.EnvironmentName}");
                        logger.LogInformation($"Consumer Brokers: {consumerOptions.Brokers}");
                        logger.LogInformation($"Consumer Brokers: {producerOptions.Brokers}");
                        return logger;
                    });

                    // Add language store
                    var languageStore = new ConcurrentDictionary<int, string>
                    {
                        [1] = "Ciao",
                        [2] = "Bonjour",
                        [3] = "Hola",
                        [4] = "Hello",
                    };
                    services.AddSingleton<IDictionary<int, string>>(languageStore);

                    // TODO: Add event processor

                    // Add worker
                    services.AddHostedService<KafkaWorker>();
                });
            return builder;
        }
    }
}