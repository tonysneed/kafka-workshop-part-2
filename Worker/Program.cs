using EventStreamProcessing.Abstractions;
using EventStreamProcessing.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Worker.Handlers;
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

                    // Add event processor
                    services.AddSingleton<IEventProcessor>(sp =>
                    {
                        // Get logger
                        var logger = sp.GetRequiredService<ILogger>();

                        // Create raw-events consumer
                        var kafkaConsumer = KafkaUtils.CreateConsumer(consumerOptions.Brokers, 
                            consumerOptions.TopicsList, logger);

                        // Create validation-error-events producer
                        var kafkaErrorProducer = KafkaUtils.CreateProducer(producerOptions.Brokers, logger);

                        // Create processed-events producer
                        var kafkaFinalProducer = KafkaUtils.CreateProducer(producerOptions.Brokers, logger);

                        // Create chain of handlers
                        var handlers = new List<MessageHandler>
                        {
                            new ValidationHandler(languageStore, new KafkaEventProducer<int, string>
                                (kafkaErrorProducer, producerOptions.ValidationTopic, logger), logger),
                            new EnrichmentHandler(languageStore, logger),
                            new FilterHandler(m => !m.Value.Contains("Hello"), logger) // Filter out English greetings
                        };

                        // Create event processor
                        return new KafkaEventProcessor<int, string, int, string>(
                            new KafkaEventConsumer<int, string>(kafkaConsumer, logger),
                            new KafkaEventProducer<int, string>(kafkaFinalProducer, producerOptions.FinalTopic, logger),
                            handlers.ToArray());
                    });

                    // Add worker
                    services.AddHostedService<KafkaWorker>();
                });
            return builder;
        }
    }
}