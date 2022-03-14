using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace PeriodicBackgroundSubscriptionService {

    public class PeriodicService : BackgroundService, IObservable<Object>
        {
            public readonly Serilog.Core.Logger _logger;
            private List<IObserver<Object>> _observers;
            private int _delay;
            
            public PeriodicService(Serilog.Core.Logger logger, int delay)
            {
                _logger = logger;
                _observers = new List<IObserver<Object>>();
                _delay=delay;
            }

            //Allows outside classes to subscribe
            public IDisposable Subscribe(IObserver<Object> observer)
            {
                if (!_observers.Contains(observer))
                {
                    _observers.Add(observer);
                }
                return new Unsubscriber<Object>(_observers, observer);
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
                    Object n = new Object();
                    foreach (IObserver<Object> observer in _observers){
                        observer.OnNext(n);
                    }
                    await Task.Delay(_delay, stoppingToken);
                }
                    
            }

            public override void Dispose()
            {
                base.Dispose();
            }
        }

    public class P1 : PeriodicService {
        public P1(Serilog.Core.Logger logger, int delay) : base(logger, delay) {

        }
    }

     public class P2 : PeriodicService {
        public P2(Serilog.Core.Logger logger, int delay) : base(logger, delay) {
            
        }
    }
    
    public class Unsubscriber<Object> : IDisposable
        {
            private List<IObserver<Object>> _observers;
            private IObserver<Object> _observer;

            internal Unsubscriber(List<IObserver<Object>> observers,
                                    IObserver<Object> observer)
            {
                this._observers = observers;
                this._observer = observer;
            }

            public void Dispose()
            {
                if (_observers.Contains(_observer))
                    _observers.Remove(_observer);
            }
        } 

}