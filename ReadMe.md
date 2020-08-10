# Kafka Workshop Part 2: Stream Processing

Event Stream Processing

### Prerequisites

1. Install [Docker Desktop](https://docs.docker.com/desktop/).
   - You will need at least 8 GB of available memory.
2. Open a terminal at the project root and run `docker-compose up --build -d`.
   - To check the running containers run `docker-compose ps`.
   - To bring down the containers run `docker-compose down`.
3. Open a browser to http://localhost:9021/.
   - Verify the cluster is healthy. (This may take a few minutes.)

> **Note**: Switch to the `after` branch to view the completed solution: `git checkout after`

## Producer

1. Add `Confluent.Kafka` package.
2. Add `Run_Producer` method.

```csharp
private static async Task Run_Producer(string brokerList, string topicName, CancellationToken cancellationToken)
{
    var config = new ProducerConfig { BootstrapServers = brokerList };

    using (var producer = new ProducerBuilder<int, string>(config).Build())
    {
        Console.WriteLine("\n-----------------------------------------------------------------------");
        Console.WriteLine($"Producer {producer.Name} producing on topic {topicName} to brokers {brokerList}.");
        Console.WriteLine("-----------------------------------------------------------------------");
        Console.WriteLine("To create a kafka message with integer key and string value:");
        Console.WriteLine("> key value<Enter>");
        Console.WriteLine("Ctrl-C then <Enter> to quit.\n");

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");

            string text;
            try
            {
                text = Console.ReadLine();
            }
            catch (IOException)
            {
                // IO exception is thrown when ConsoleCancelEventArgs.Cancel == true.
                break;
            }
            if (text == null || text.Length == 0)
            {
                // Console returned null before 
                // the CancelKeyPress was treated
                continue;
            }

            int key = 0;
            string val = text;

            // split line if both key and value specified.
            int index = text.IndexOf(" ");
            if (index != -1)
            {
                key = int.Parse(text.Substring(0, index));
                val = text.Substring(index + 1);
            }

            try
            {
                // Note: Awaiting the asynchronous produce request below prevents flow of execution
                // from proceeding until the acknowledgement from the broker is received (at the 
                // expense of low throughput).
                var deliveryReport = await producer.ProduceAsync(
                    topicName, new Message<int, string> { Key = key, Value = val });

                Console.WriteLine($"delivered to: {deliveryReport.TopicPartitionOffset}");
            }
            catch (ProduceException<int, string> e)
            {
                Console.WriteLine($"failed to deliver message: {e.Message} [{e.Error.Code}]");
            }
        }

        // Since we are producing synchronously, at this point there will be no messages
        // in-flight and no delivery reports waiting to be acknowledged, so there is no
        // need to call producer.Flush before disposing the producer.
    }
}
```

3. Call `Run_Producer` method.

```csharp
static async Task Main(string[] args)
{
    CancellationTokenSource cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => {
        e.Cancel = true; // prevent the process from terminating.
        cts.Cancel();
    };

    await Run_Producer("localhost:9092", "raw-events", cts.Token);
}
```

## Consumer

1. Add `Confluent.Kafka` package.
2. Create `Run_Consumer` method.

```csharp
public static void Run_Consumer(string brokerList, List<string> topics, CancellationToken cancellationToken)
{
    var config = new ConsumerConfig
    {
        BootstrapServers = brokerList,
        GroupId = "csharp-consumer",
        EnableAutoCommit = false,
        StatisticsIntervalMs = 5000,
        SessionTimeoutMs = 6000,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnablePartitionEof = true
    };

    const int commitPeriod = 5;

    // Note: If a key or value deserializer is not set (as is the case below), the 
    // deserializer corresponding to the appropriate type from Confluent.Kafka.Deserializers
    // will be used automatically (where available). The default deserializer for string
    // is UTF8. The default deserializer for Ignore returns null for all input data
    // (including non-null data).
    using (var consumer = new ConsumerBuilder<int, string>(config)
        // Note: All handlers are called on the main .Consume thread.
        .SetErrorHandler((_, e) => Console.WriteLine($"Error: {e.Reason}"))
        // .SetStatisticsHandler((_, json) => Console.WriteLine($"Statistics: {json}"))
        .SetPartitionsAssignedHandler((c, partitions) =>
        {
            Console.WriteLine($"Assigned partitions: [{string.Join(", ", partitions)}]");
            // possibly manually specify start offsets or override the partition assignment provided by
            // the consumer group by returning a list of topic/partition/offsets to assign to, e.g.:
            // 
            // return partitions.Select(tp => new TopicPartitionOffset(tp, externalOffsets[tp]));
        })
        .SetPartitionsRevokedHandler((c, partitions) =>
        {
            Console.WriteLine($"Revoking assignment: [{string.Join(", ", partitions)}]");
        })
        .Build())
    {
        consumer.Subscribe(topics);

        try
        {
            while (true)
            {
                try
                {
                    var consumeResult = consumer.Consume(cancellationToken);

                    if (consumeResult.IsPartitionEOF)
                    {
                        Console.WriteLine(
                            $"Reached end of topic {consumeResult.Topic}, partition {consumeResult.Partition}, offset {consumeResult.Offset}.");

                        continue;
                    }

                    Console.WriteLine($"Received message at {consumeResult.TopicPartitionOffset}: {consumeResult.Message.Value}");

                    if (consumeResult.Offset % commitPeriod == 0)
                    {
                        // The Commit method sends a "commit offsets" request to the Kafka
                        // cluster and synchronously waits for the response. This is very
                        // slow compared to the rate at which the consumer is capable of
                        // consuming messages. A high performance application will typically
                        // commit offsets relatively infrequently and be designed handle
                        // duplicate messages in the event of failure.
                        try
                        {
                            consumer.Commit(consumeResult);
                        }
                        catch (KafkaException e)
                        {
                            Console.WriteLine($"Commit error: {e.Error.Reason}");
                        }
                    }
                }
                catch (ConsumeException e)
                {
                    Console.WriteLine($"Consume error: {e.Error.Reason}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Closing consumer.");
            consumer.Close();
        }
    }
}
```

3. Call the `Run_Consumer` method.

```csharp
static void Main(string[] args)
{
    CancellationTokenSource cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => {
        e.Cancel = true; // prevent the process from terminating.
        cts.Cancel();
    };

    Run_Consumer("localhost:9092", new List<string> { "raw-events" }, cts.Token);
}
```
