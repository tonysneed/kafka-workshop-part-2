using System.Collections.Generic;

namespace Worker.Options
{
    public class ConsumerOptions
    {
        public string Brokers { get; set; }
        public List<string> TopicsList { get; set; }
    }
}
