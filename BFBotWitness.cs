using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;
using BFBotWitness;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Threading;

namespace BFBotWitness {

    public interface IBFBotProducer : IObservable<Object> {
        Task Run(CancellationToken token);
    }

    public interface IBFBotConsumer : IObserver<Object> {

    }

    // Abstract base class, All Producers should inherit from it
    public class BFBotProducer : IBFBotProducer {
        public String Id;

        public BFBotProducer(String id) {
            Id=id;
        }

        public virtual IDisposable Subscribe(IObserver<Object> observer)
        {
            return null;
        }

        public virtual async Task Run(CancellationToken token) {

        }
    }

    public class IdExistsException : Exception {
        public IdExistsException() {

        }


        public IdExistsException(String id) : base(id) {

        }

        public IdExistsException(String id, Exception inner) : base (id, inner) {

        }
    }

    public class BFBotProducerConfig {
        private readonly String _id;
        private readonly BFBotProducer _worker;
        private readonly int _period;

        public BFBotProducerConfig(String id, BFBotProducer worker, int period) {
            _id=id;
            _worker=worker;
            _period=period;
        }

        public String Id { get { return _id; } }
        public BFBotProducer Worker { get { return _worker; } }
        public int Period { get { return _period; } }
    }

    public class BFBotConsumerConfig {
        private readonly String _id;
        private readonly IBFBotConsumer _worker;
        private readonly List<String> _eventIds;

        public BFBotConsumerConfig(String id, IBFBotConsumer worker, List<String> eventIds) {
            _id=id;
            _worker=worker;
            _eventIds=eventIds;
        }

        public String Id { get { return _id; } }
        public IBFBotConsumer Worker { get { return _worker; } }
        public List<String> EventIds { get { return _eventIds; } }
    }

    public class BFBotEvent {
        private String _id;
        private Object _payload;

        public BFBotEvent(String id, Object payload) {
            _id=id;
            _payload=payload;
        }

        public String Id { get { return _id; } }
        public Object Payload { get { return _payload; } }
    }

    public class BFBotWitnessEngine : BackgroundService, IObserver<Object> {
        private readonly Serilog.Core.Logger _logger;
        private readonly int _delay;
        private readonly List<String> _producerIds;
        private readonly List<String> _consumerIds;
        private readonly Dictionary<String, BFBotProducerConfig> _producers;
        private readonly Dictionary<String, BFBotConsumerConfig> _consumers;
        private readonly Dictionary<String, Stopwatch> _timers;
        private readonly Dictionary<String, CancellationToken> _tokens;

        public BFBotWitnessEngine(Serilog.Core.Logger logger, int delay) {
             _logger = logger;
            _delay=delay;
            _producerIds = new List<String>();
            _consumerIds = new List<String>();
            _producers= new Dictionary<String, BFBotProducerConfig>();
            _consumers = new Dictionary<String, BFBotConsumerConfig>();
            _timers = new Dictionary<String, Stopwatch>();
            _tokens = new Dictionary<String, CancellationToken>();
        }

        public void AttachProducer(BFBotProducerConfig config) {
            if (!(this._producerIds.Contains(config.Id))) {
                _producerIds.Add(config.Id);
                _producers[config.Id]=config;
                _timers[config.Id]=Stopwatch.StartNew();
                IDisposable u = config.Worker.Subscribe(this);
            } else {
                throw new IdExistsException();
            }
        }

        public void AttachConsumer(BFBotConsumerConfig config) {
            if (!(this._consumerIds.Contains(config.Id))) {
                _consumerIds.Add(config.Id);
                _consumers[config.Id]=config;
            } else {
                throw new IdExistsException();
            }
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _producerIds.ForEach(id => MaybeExecuteProducer(id));
                await Task.Delay(_delay, stoppingToken);
            }
                
        }

        public void OnNext(Object o) {
            BFBotEvent e = (BFBotEvent)o;
            HandleEvent(e);
        }

        public void OnCompleted() {

        }

        public void OnError(Exception e) {

        }

        private void HandleEvent(BFBotEvent e) {
            Console.WriteLine(e.Id);
            for (int i=0; i < _consumerIds.Count; i++) {
                BFBotConsumerConfig c = _consumers[_consumerIds[i]];
                if (c.EventIds.Contains(e.Id)) {
                    c.Worker.OnNext(e);
                }
            }
        }

        private void MaybeExecuteProducer(String id) {
            Stopwatch timer = _timers[id];
            BFBotProducerConfig c = _producers[id];
            if (timer.ElapsedMilliseconds > c.Period) {
                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken token = source.Token;
                _tokens[id]=token;
                // Allow the engine loop to keep running by executing producer
                // logic in seperate threads
                Task t = Task.Run(() => c.Worker.Run(token));
                _timers[id]= Stopwatch.StartNew();
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}