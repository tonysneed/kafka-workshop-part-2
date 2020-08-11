# Kafka Workshop Part 2: Stateless Stream Processing

In this exercise you will create a .NET Core worker service to perform stateless (single-event) stream processing. By adding a reference to the `EventStreamProcessing.Kafka` package, you will be able to register a `KafkaEventProcessor` that accepts `KafkaEventConsumer`, `KafkaEventProducer`, and an array of `MessageHandler` to validate, enrich and filter messages.

When the **Producer** console app sends messages to the "raw-events" topics, the event processor will read these messages, sending them through the chain of message handlers you have configured, then write messages to the "processed-events" topic. The **Consumer** console app will read and display the processed messages.

> **Note**: Here is a blog post explaining the design of  the event stream processing framework: https://blog.tonysneed.com/2020/06/25/event-stream-processing-micro-framework-apache-kafka/.

### Prerequisites

1. Install [Docker Desktop](https://docs.docker.com/desktop/).
   - You will need at least 8 GB of available memory.
2. Open a terminal at the project root and run `docker-compose up --build -d`.
   - To check the running containers run `docker-compose ps`.
   - To bring down the containers run `docker-compose down`.
3. Open a browser to http://localhost:9021/.
   - Verify the cluster is healthy. (This may take a few minutes.)

> **Note**: Switch to the `after` branch to view the completed solution: `git checkout after`

## Worker Application

1. Add NuGet packages to the **Worker** project.
    ```bash
    cd Worker
    dotnet add package Confluent.Kafka
    dotnet add package Microsoft.Extensions.Hosting
    dotnet add package EventStreamProcessing.Kafka
    ```
2. Create validation, enrichment and filter message handlers.
   - Remove **Placeholder.txt** from the **Handlers** folder.
   - Add a `ValidationHandler` class to the **Handlers** folder that extends `MessageHandler`.
     - Add a **ctor** to inject `languageStore`, `validationErrorProducer`, `logger`.
    ```csharp
    public class ValidationHandler : MessageHandler
    {
        private readonly IDictionary<int, string> languageStore;
        private readonly IEventProducer<Confluent.Kafka.Message<int, string>> validationErrorProducer;
        private readonly ILogger logger;

        public ValidationHandler(IDictionary<int, string> languageStore,
            IEventProducer<Confluent.Kafka.Message<int, string>> validationErrorProducer,
            ILogger logger)
        {
            this.languageStore = languageStore;
            this.validationErrorProducer = validationErrorProducer;
            this.logger = logger;
        }
    }
    ```
     - Override `HandleMessage` with an `async` method to validate the message key corresponds to an entry in the language store, producing an error event if validation fails.
    ```csharp
    public override async Task<Message> HandleMessage(Message sourceMessage)
    {
        // Validate supported language
        // For simplicity, message key corresponds to selected language
        var message = (Message<int, string>)sourceMessage;
        bool validationPassed;
        if (languageStore.ContainsKey(message.Key))
        {
            validationPassed = true;
        }
        else
        {
            var errorMessage = $"No language corresponds to message key '{message.Key}'";
            validationErrorProducer.ProduceEvent(
                new Confluent.Kafka.Message<int, string>
                {
                    Key = message.Key,
                    Value = errorMessage
                });
            logger.LogInformation($"Validation handler: {errorMessage}");
            return null;
        }

        // Log result and call next handler
        var sinkMessage = new Message<int, string>(message.Key, message.Value);
        if (validationPassed)
        {
            logger.LogInformation($"Validation handler: Passed { sinkMessage.Key } { sinkMessage.Value }");
        }
        return await base.HandleMessage(sinkMessage);
    }
    ```
   - Add an `EnrichmentHandler` class to the **Handlers** folder that extends `MessageHandler`.
     - Add a **ctor** to inject `languageStore`, `logger`.
    ```csharp
    public class EnrichmentHandler : MessageHandler
    {
        private readonly IDictionary<int, string> languageStore;
        private readonly ILogger logger;

        public EnrichmentHandler(IDictionary<int, string> languageStore, ILogger logger)
        {
            this.languageStore = languageStore;
            this.logger = logger;
        }
    }
    ```
     - Override `HandleMessage` with an `async` method to enrich the message by replacing "Hello" with a translated greeting.
    ```csharp
    public override async Task<Message> HandleMessage(Message sourceMessage)
    {
        // Get greeting in supported language 
        // For simplicity, message key corresponds to selected language
        var message = (Message<int, string>)sourceMessage;
        var value = message.Value;
        if (languageStore.TryGetValue(message.Key, out string greeting))
        {
            value = message.Value.Replace("Hello", greeting);
        }

        // Call next handler
        var sinkMessage = new Message<int, string>(message.Key, value);
        logger.LogInformation($"Enrichment handler: {sinkMessage.Key} {sinkMessage.Value }");
        return await base.HandleMessage(sinkMessage);
    }
    ```
   - Add an `FilterHandler` class to the **Handlers** folder that extends `MessageHandler`.
     - Add a **ctor** to inject `logger` and a `Func` that accepts a `Message` and returns `bool` so that the caller can specify a filtering strategy.
    ```csharp
    public class FilterHandler : MessageHandler
    {
        private readonly Func<Message<int, string>, bool> filter;
        private readonly ILogger logger;

        public FilterHandler(Func<Message<int, string>, bool> filter, ILogger logger)
        {
            this.filter = filter;
            this.logger = logger;
        }
    }
    ```
     - Override `HandleMessage` with an `async` method to filter the message by supplied strategy.
    ```csharp
    public override async Task<Message> HandleMessage(Message sourceMessage)
    {
        // Filter message
        var message = (Message<int, string>)sourceMessage;
        if (!filter(message))
        {
            logger.LogInformation($"Filter handler: Excluded { message.Key } { message.Value }");
            return null;
        }

        // Call next handler
        var sinkMessage = new Message<int, string>(message.Key, message.Value);
        logger.LogInformation($"Filter handler: Accepted { sinkMessage.Key } { sinkMessage.Value }");
        return await base.HandleMessage(sinkMessage);
    }
    ```

3. Register `IEventProcessor` with the dependency injection system in `Program.CreateHostBuilder`.
   - Inside `Host.CreateDefaultBuilder.ConfigureServices` call `services.AddSingleton<IEventProcessor>` on line 54 in **Program.cs**, passing a lambda in which you resolve the necessary dependencies.
    ```csharp
    // Add event processor
    services.AddSingleton<IEventProcessor>(sp =>
    {

    });
    ```
   - Inside the lamda statement, get `ILogger` from the DI system.
    ```csharp
    // Get logger
    var logger = sp.GetRequiredService<ILogger>();
    ```
   - Use `KafkaUtils` to create a consumer to read the "raw-events" topic.
    ```csharp
    // Create raw-events consumer
    var kafkaConsumer = KafkaUtils.CreateConsumer(consumerOptions.Brokers, 
        consumerOptions.TopicsList, logger);
    ```
   - Use `KafkaUtils` to create a producer to write to the "validation-error-events" topic.
    ```csharp
    // Create validation-error-events producer
    var kafkaErrorProducer = KafkaUtils.CreateProducer(producerOptions.Brokers, logger);
    ```
   - Use `KafkaUtils` to create a producer to write to the "processed-events" topic.
    ```csharp
    // Create processed-events producer
    var kafkaFinalProducer = KafkaUtils.CreateProducer(producerOptions.Brokers, logger);
    ```
   - Create the handlers collection.
    ```csharp
    // Create handlers
    var handlers = new List<MessageHandler>
    {
        new ValidationHandler(languageStore, new KafkaEventProducer<int, string>
            (kafkaErrorProducer, producerOptions.ValidationTopic, logger), logger),
        new EnrichmentHandler(languageStore, logger),
        new FilterHandler(m => !m.Value.Contains("Hello"), logger) // Filter out English greetings
    };
    ```
   - Lastly, return a new `KafkaEventProcessor`, passing a new `KafkaEventConsumer`, new `KafkaEventProducer`, and the `handlers` array.
    ```csharp
    // Create event processor
    return new KafkaEventProcessor<int, string, int, string>(
        new KafkaEventConsumer<int, string>(kafkaConsumer, logger),
        new KafkaEventProducer<int, string>(kafkaFinalProducer, producerOptions.FinalTopic, logger),
        handlers.ToArray());
    ```

4. Update `KafkaWorker` class.
   - Inject `IEventProcessor` into the **ctor**.
   - In `ExecuteAsync` call `eventProcessor.Process` in the `while` loop.
    ```csharp
    public class KafkaWorker : BackgroundService
    {
        private readonly IEventProcessor eventProcessor;
        private readonly ILogger logger;

        public KafkaWorker(IEventProcessor eventProcessor, ILogger logger)
        {
            this.eventProcessor = eventProcessor;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Worker processing event at: {time}", DateTimeOffset.Now);

                // Process event
                await eventProcessor.Process(cancellationToken);
            }
        }
    }
    ```

## Usage

1. Start the **Consumer** console app (Ctrl+F5).

2. Set breakpoints in the **Worker** project to view the process flow.
   - Set breakpoints in the `HandleMessage` method of each handler.
   - Set a breakpoint in `eventProcessor.Process` in `KafkaWorker.ExecuteAsync`.
   - Set a breakpoint in `Host.CreateDefaultBuilder.ConfigureServices` in `Program.CreateHostBuilder`.
   - Press F5 to start the debugger.

3. Start the **Producer** console app (Ctrl+F5).
   - Enter the following values.
    ```
    1 Hello World
    2 Hello World
    3 Hello World
    4 Hello World
    5 Hello World
    ```

4. Note processing of the event stream for each message in the "raw-events" topic.
   - Notice that messages 1, 2 and 3 are all translated.
   - Notice that message 4 is filtered out because it is English.
   - Notice that message 5 fails validation because no language corresponds to message key 5.