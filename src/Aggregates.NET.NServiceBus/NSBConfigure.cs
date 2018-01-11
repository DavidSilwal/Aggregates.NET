﻿using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NServiceBus.Transport;

namespace Aggregates
{
    public static class NSBConfigure
    {
        public static Configure NServiceBus(this Configure config, EndpointConfiguration endpointConfig)
        {
            {
                var settings = endpointConfig.GetSettings();
                var conventions = endpointConfig.Conventions();

                // set the configured endpoint name to the one NSB config was constructed with
                config.SetEndpointName(settings.Get<string>("NServiceBus.Routing.EndpointName"));

                conventions.DefiningCommandsAs(type => typeof(Messages.ICommand).IsAssignableFrom(type));
                conventions.DefiningEventsAs(type => typeof(Messages.IEvent).IsAssignableFrom(type));
                conventions.DefiningMessagesAs(type => typeof(Messages.IMessage).IsAssignableFrom(type));

                endpointConfig.AssemblyScanner().ScanAppDomainAssemblies = true;
                endpointConfig.EnableCallbacks();
                endpointConfig.EnableInstallers();

                endpointConfig.UseSerialization<Internal.AggregatesSerializer>();
                endpointConfig.EnableFeature<Feature>();
            }




            config.SetupTasks.Add((c) =>
            {
                var settings = endpointConfig.GetSettings();

                settings.Set("Retries", config.Retries);
                settings.Set("SlowAlertThreshold", config.SlowAlertThreshold);

                // Set immediate retries to our "MaxRetries" setting
                endpointConfig.Recoverability().Immediate(x =>
                {
                    x.NumberOfRetries(config.Retries);
                });

                endpointConfig.Recoverability().Delayed(x =>
                {
                    x.NumberOfRetries(config.Retries);
                    x.TimeIncrease(TimeSpan.FromSeconds(5));
                });

                endpointConfig.MakeInstanceUniquelyAddressable(c.UniqueAddress);
                endpointConfig.LimitMessageProcessingConcurrencyTo(c.ParallelMessages);
                // NSB doesn't have an endpoint name setter other than the constructor, hack it in
                settings.Set("NServiceBus.Routing.EndpointName", c.Endpoint);

                return Aggregates.Bus.Start(endpointConfig);
            });

            return config;
        }

    }
}
