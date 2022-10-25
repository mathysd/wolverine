using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Runtime.Routing;
using Wolverine.Transports;
using Wolverine.Transports.Sending;

namespace Wolverine.RabbitMQ.Internal
{
    internal interface IRabbitMqEndpoint
    {
        
    }
    
    internal class RabbitMqSender : RabbitMqConnectionAgent, ISender
    {
        private readonly string _exchangeName;
        private readonly bool _isDurable;
        private readonly string _key;
        private Func<Envelope, string> _toRoutingKey;
        private readonly IEnvelopeMapper<IBasicProperties, IBasicProperties> _mapper;
        private readonly RabbitMqEndpoint _queue;

        public RabbitMqSender(RabbitMqEndpoint queue, RabbitMqTransport transport,
            RoutingMode routingType, IWolverineRuntime runtime) : base(
            transport.SendingConnection, queue, runtime.Logger)
        {
            Destination = queue.Uri;

            _isDurable = queue.Mode == EndpointMode.Durable;

            _exchangeName = queue.ExchangeName;
            _key = queue.RoutingKey();

            _toRoutingKey = routingType == RoutingMode.Static ? _ => _key : TopicRouting.DetermineTopicName;

            _mapper = queue.BuildMapper(runtime);
            _queue = queue;
            
            EnsureConnected();
        }

        public bool SupportsNativeScheduledSend => false;
        public Uri Destination { get; }

        public async ValueTask SendAsync(Envelope envelope)
        {
            await _queue.InitializeAsync(_logger);

            if (State == AgentState.Disconnected)
            {
                throw new InvalidOperationException($"The RabbitMQ agent for {Destination} is disconnected");
            }

            var props = Channel.CreateBasicProperties();
            props.Persistent = _isDurable;
            props.Headers = new Dictionary<string, object>();

            _mapper.MapEnvelopeToOutgoing(envelope, props);

            var routingKey = _toRoutingKey(envelope);
            Channel.BasicPublish(_exchangeName, routingKey, props, envelope.Data);
        }

        public Task<bool> PingAsync()
        {
            lock (Locker)
            {
                if (State == AgentState.Connected)
                {
                    return Task.FromResult(true);
                }

                startNewChannel();

                if (Channel.IsOpen)
                {
                    return Task.FromResult(true);
                }

                teardownChannel();
                return Task.FromResult(false);
            }
        }
    }
}
