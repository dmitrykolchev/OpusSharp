// <copyright file="Observable.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Concurrent;
using Neutrino.Psi.Components;

namespace Neutrino.Psi;

/// <summary>
/// Extension methods that simplify operator usage.
/// </summary>
public static partial class Operators
{
    /// <summary>
    /// Convert a stream to an <see cref="IObservable{T}"/>.
    /// </summary>
    /// <typeparam name="T">Type of messages for the source stream.</typeparam>
    /// <param name="stream">The source stream.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    /// <param name="name">An optional name for this stream operator.</param>
    /// <returns>Observable with elements from the source stream.</returns>
    public static IObservable<T> ToObservable<T>(this IProducer<T> stream, DeliveryPolicy<T> deliveryPolicy = null, string name = nameof(ToObservable))
    {
        return new StreamObservable<T>(stream, deliveryPolicy, name);
    }

    /// <summary>
    /// Observable stream class.
    /// </summary>
    /// <typeparam name="T">Type of stream messages.</typeparam>
    public class StreamObservable<T> : IObservable<T>
    {
        private readonly ConcurrentDictionary<IObserver<T>, IObserver<T>> observers = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamObservable{T}"/> class.
        /// </summary>
        /// <param name="stream">The source stream to observe.</param>
        /// <param name="deliveryPolicy">An optional delivery policy.</param>
        /// <param name="name">An optional name for this stream operator.</param>
        public StreamObservable(IProducer<T> stream, DeliveryPolicy<T> deliveryPolicy = null, string name = nameof(StreamObservable<T>))
        {
            Processor<T, T> processor = new(
                stream.Out.Pipeline,
                (d, e, s) =>
                {
                    foreach (System.Collections.Generic.KeyValuePair<IObserver<T>, IObserver<T>> obs in observers)
                    {
                        obs.Value.OnNext(d.DeepClone());
                    }

                    s.Post(d, e.OriginatingTime);
                },
                name: name);

            stream.Out.PipeTo(processor, deliveryPolicy);

            processor.In.Unsubscribed += _ =>
            {
                foreach (System.Collections.Generic.KeyValuePair<IObserver<T>, IObserver<T>> obs in observers)
                {
                    obs.Value.OnCompleted();
                }
            };
        }

        /// <summary>
        /// Gets a value indicating whether this observable stream has subscribers.
        /// </summary>
        public bool HasSubscribers => observers.Count > 0;

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<T> observer)
        {
            observers.TryAdd(observer, observer);
            return new Unsubscriber(this, observer);
        }

        private class Unsubscriber : IDisposable
        {
            private readonly StreamObservable<T> observable;
            private readonly IObserver<T> observer;

            public Unsubscriber(StreamObservable<T> observable, IObserver<T> observer)
            {
                this.observable = observable;
                this.observer = observer;
            }

            public void Dispose()
            {
                if (observer != null)
                {
                    observable.observers.TryRemove(observer, out _);
                }
            }
        }
    }
}
