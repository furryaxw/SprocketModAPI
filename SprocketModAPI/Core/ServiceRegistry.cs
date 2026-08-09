using System;
using System.Collections.Generic;

namespace SprocketModAPI
{
    internal sealed class ServiceRegistry : IDisposable
    {
        private readonly Action<string> error;
        private readonly Dictionary<Type, Registration> services = new();
        private bool disposed;

        internal ServiceRegistry(Action<string> error)
        {
            this.error = error ?? throw new ArgumentNullException(nameof(error));
        }

        internal IDisposable Register<T>(T service) where T : class
        {
            ThrowIfDisposed();
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            Type serviceType = typeof(T);
            if (services.ContainsKey(serviceType))
            {
                string message = $"[SMA] Duplicate service registration rejected: {serviceType.FullName}.";
                error(message);
                throw new InvalidOperationException(message);
            }

            var registration = new Registration(this, serviceType, service);
            services.Add(serviceType, registration);
            return registration;
        }

        internal bool TryGet<T>(out T? service) where T : class
        {
            if (!disposed && services.TryGetValue(typeof(T), out Registration? registration))
            {
                service = (T)registration.Service;
                return true;
            }

            service = null;
            return false;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            Registration[] registrations = new List<Registration>(services.Values).ToArray();
            services.Clear();
            var disposedInstances = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (Registration registration in registrations)
            {
                registration.Detach();
                if (disposedInstances.Add(registration.Service) && registration.Service is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private void Unregister(Registration registration)
        {
            if (services.TryGetValue(registration.ServiceType, out Registration? current)
                && ReferenceEquals(current, registration))
                services.Remove(registration.ServiceType);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ServiceRegistry));
        }

        private sealed class Registration : IDisposable
        {
            private ServiceRegistry? registry;

            internal Registration(ServiceRegistry registry, Type serviceType, object service)
            {
                this.registry = registry;
                ServiceType = serviceType;
                Service = service;
            }

            internal Type ServiceType { get; }
            internal object Service { get; }

            public void Dispose()
            {
                ServiceRegistry? owner = registry;
                if (owner == null)
                    return;
                registry = null;
                owner.Unregister(this);
                if (Service is IDisposable disposable)
                    disposable.Dispose();
            }

            internal void Detach() => registry = null;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new();
            public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
