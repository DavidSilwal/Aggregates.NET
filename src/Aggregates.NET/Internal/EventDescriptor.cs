﻿using System;
using System.Collections.Generic;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    public class EventDescriptor : IEventDescriptor
    {
        public Guid EventId { get; set; }
        public string EntityType { get; set; }

        public bool Compressed { get; set; }
        public int Version { get; set; }
        public DateTime Timestamp { get; set; }

        public IDictionary<string, string> Headers { get; set; }
    }
}