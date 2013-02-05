﻿namespace ServiceBus.Management.AcceptanceTests
{
    using System;
    using Contexts;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NUnit.Framework;

    [TestFixture]
    public class When_a_message_has_been_successfully_processed : AcceptanceTest
    {
        [Test]
        public void Should_be_imported_and_published_be_the_api()
        {
            var context = new MyContext();

            Scenario.Define(() => context)
                .WithEndpoint<ManagementEndpoint>()
                .WithEndpoint<Sender>()
                .WithEndpoint<Receiver>()
                .Done(c =>
                    {
                        lock (context)
                        {
                            if (c.ApiData != null)
                                return true;

                            if (c.MessageId == null)
                                return false;

                            c.ApiData = ApiCall("/messages/" + context.MessageId + ".json");


                            return true;
                        }
                    })
                .Run();

            Assert.NotNull(context.ApiData);
        }

       

        public class Sender : EndpointBuilder
        {
            public Sender()
            {
                EndpointSetup<DefaultServer>()
                    .AddMapping<MyMessage>(typeof(Receiver))
                    .When(bus => bus.Send(new MyMessage()));
            }
        }

        public class Receiver : EndpointBuilder
        {
            public Receiver()
            {
                EndpointSetup<DefaultServer>()
                    .AuditTo(Address.Parse("audit"));
            }


            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyContext Context { get; set; }

                public IBus Bus { get; set; }

                public void Handle(MyMessage message)
                {
                    Context.MessageId = Bus.CurrentMessageContext.Id;
                }
            }
        }

        [Serializable]
        public class MyMessage : ICommand
        {
        }


        public class MyContext : ScenarioContext
        {
            public string MessageId { get; set; }
            public string ApiData { get; set; }
        }
    }
}