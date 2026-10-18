namespace Agitprop.Consumer.Consumers
{
    using Agitprop.Scraper.Consumer.Consumers;

    using MassTransit;

    using Microsoft.Extensions.Configuration;

    /// <summary>
    /// Defines the configuration for the <see cref="NewsfeedJobConsumer"/>.
    /// </summary>
    public class NewsfeedJobConsumerDefinition : ConsumerDefinition<NewsfeedJobConsumer>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NewsfeedJobConsumerDefinition"/> class.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        public NewsfeedJobConsumerDefinition(IConfiguration configuration)
        {
            var concurLimit = configuration.GetValue<int?>("ConcurrencyLimit");
            if (concurLimit.HasValue) ConcurrentMessageLimit = concurLimit;
        }

        /// <summary>
        /// Configures the consumer and its endpoint.
        /// </summary>
        /// <param name="endpointConfigurator">The endpoint configurator.</param>
        /// <param name="consumerConfigurator">The consumer configurator.</param>
        /// <param name="context">The registration context.</param>
        protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<NewsfeedJobConsumer> consumerConfigurator, IRegistrationContext context)
        {
            endpointConfigurator.UseInMemoryOutbox(context);
        }
    }
}
