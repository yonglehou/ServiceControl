﻿namespace ServiceBus.Management.AcceptanceTests.Contexts
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using NLog;
    using NLog.Config;
    using NLog.Targets;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting.Support;
    using NServiceBus.Config.ConfigurationSource;
    using NServiceBus.Features;
    using NServiceBus.Hosting.Helpers;
    using NServiceBus.Logging.Loggers.NLogAdapter;
    using TransportIntegration;

    public class DefaultServerWithoutAudit : DefaultServer
    {
        public override void AddMoreConfig()
        {
            Configure.Features.Disable<Audit>();
        }
    }

    public class DefaultServer : IEndpointSetupTemplate
    {
        public virtual void AddMoreConfig()
        {
            
        }

        public virtual void SetSerializer(Configure configure)
        {
            //NOOP Default is XML serializer
        }

        public Configure GetConfiguration(RunDescriptor runDescriptor, EndpointConfiguration endpointConfiguration,
            IConfigurationSource configSource)
        {
            var settings = runDescriptor.Settings;

            var types = GetTypesToUse(endpointConfiguration);

            var transportToUse = AcceptanceTest.GetTransportIntegrationFromEnvironmentVar();
            SetupLogging(endpointConfiguration);

            Configure.Features.Enable<Sagas>();
            Configure.ScaleOut(_ => _.UseSingleBrokerQueue());

            AddMoreConfig();

            var config = Configure.With(types)
                .DefiningEventsAs(t => typeof(IEvent).IsAssignableFrom(t) || IsExternalContract(t))
                .DefineEndpointName(endpointConfiguration.EndpointName)
                .CustomConfigurationSource(configSource)
                .DefineBuilder(settings.GetOrNull("Builder"));

            SetSerializer(config);

            config
                .DefineTransport(transportToUse)
                .InMemorySagaPersister();


            if (transportToUse == null || transportToUse is MsmqTransportIntegration || transportToUse is SqlServerTransportIntegration ||
                transportToUse is RabbitMqTransportIntegration)
            {
                config.UseInMemoryTimeoutPersister();
            }

            if (transportToUse == null || transportToUse is MsmqTransportIntegration || transportToUse is SqlServerTransportIntegration)
            {
                config.InMemorySubscriptionStorage();
            }

            config.InMemorySagaPersister();

            return config.UnicastBus();
        }

        static bool IsExternalContract(Type t)
        {
            return t.Namespace != null && t.Namespace.StartsWith("ServiceControl.Contracts");
        }


        static void SetupLogging(EndpointConfiguration endpointConfiguration)
        {
            var logDir = ".\\logfiles\\";

            Directory.CreateDirectory(logDir);

            var logFile = Path.Combine(logDir, endpointConfiguration.EndpointName + ".txt");

            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }

            var logLevel = "INFO";

            var nlogConfig = new LoggingConfiguration();

            var fileTarget = new FileTarget
            {
                FileName = logFile,
            };

            nlogConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.FromString(logLevel), fileTarget));
            nlogConfig.AddTarget("debugger", fileTarget);
            NLogConfigurator.Configure(new object[] {fileTarget}, logLevel);
            LogManager.Configuration = nlogConfig;
        }

        static IEnumerable<Type> GetTypesToUse(EndpointConfiguration endpointConfiguration)
        {
            var assemblies = new AssemblyScanner().GetScannableAssemblies();

            var types = assemblies.Assemblies
                //exclude all test types by default
                                  .Where(a => a != Assembly.GetExecutingAssembly())
                                  .Where(a => a.GetName().Name != "ServiceControl")
                                  .SelectMany(a => a.GetTypes());

            types = types.Union(GetNestedTypeRecursive(endpointConfiguration.BuilderType.DeclaringType, endpointConfiguration.BuilderType));

            types = types.Union(endpointConfiguration.TypesToInclude);

            return types.Where(t => !endpointConfiguration.TypesToExclude.Contains(t)).ToList();
        }

        static IEnumerable<Type> GetNestedTypeRecursive(Type rootType, Type builderType)
        {
            yield return rootType;

            if (typeof(IEndpointConfigurationFactory).IsAssignableFrom(rootType) && rootType != builderType)
                yield break;

            foreach (var nestedType in rootType.GetNestedTypes(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SelectMany(t => GetNestedTypeRecursive(t, builderType)))
            {
                yield return nestedType;
            }
        }
    }
}