﻿using EventStreamProcessing.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Worker.Handlers
{
    public class EnrichmentHandler : MessageHandler
    {
        private readonly IDictionary<int, string> languageStore;
        private readonly ILogger logger;

        public EnrichmentHandler(IDictionary<int, string> languageStore, ILogger logger)
        {
            this.languageStore = languageStore;
            this.logger = logger;
        }

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
    }
}
