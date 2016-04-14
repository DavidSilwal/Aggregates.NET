﻿using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Unicast.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class AsyncronizedLoad : IBehavior<IncomingContext>
    {
        public IInvokeObjects ObjectInvoker { get; set; }

        private static MethodInfo _snapshotRegion = typeof(IncomingContext).GetMethod("CreateSnapshotRegion", BindingFlags.NonPublic | BindingFlags.Instance);
        private static Func<IncomingContext, IDisposable> CreateSnapshotRegion = (context) => (IDisposable)_snapshotRegion.Invoke(context, null);

        public void Invoke(IncomingContext context, Action next)
        {
            var messageToHandle = context.IncomingLogicalMessage;

            var handlerGenericType = typeof(IHandleMessagesAsync<>).MakeGenericType(messageToHandle.MessageType);
            List<dynamic> handlers = context.Builder.BuildAll(handlerGenericType).ToList();


            if (handlers.Count == 0)
            {
                var error = string.Format("No handlers could be found for message type: {0}", messageToHandle.MessageType);
                throw new InvalidOperationException(error);
            }


            foreach (var handler in handlers)
            {
                using (CreateSnapshotRegion(context))
                {
                    var lambda = ObjectInvoker.Invoker(handler, messageToHandle.MessageType);

                    var loadedHandler = new AsyncMessageHandler
                    {
                        Handler = handler,
                        Invocation = lambda
                    };

                    context.Set(loadedHandler);

                    next();

                    if (context.HandlerInvocationAborted)
                    {
                        //if the chain was aborted skip the other handlers
                        break;
                    }
                }
            }

        }
    }

    public class AsyncMessageHandler
    {
        public dynamic Handler { get; set; }
        public Func<dynamic, object, Task> Invocation { get; set; }
    }
}