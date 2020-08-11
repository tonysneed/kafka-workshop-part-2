using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Worker
{
    public class KafkaWorker : BackgroundService
    {
        private readonly ILogger logger;

        public KafkaWorker(ILogger logger)
        {
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Worker processing event at: {time}", DateTimeOffset.Now);

                // TODO: Process event
            }
        }
    }
}
