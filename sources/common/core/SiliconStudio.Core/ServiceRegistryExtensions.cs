﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;

namespace SiliconStudio.Core
{
    public static class ServiceRegistryExtensions
    {
        /// <summary>
        /// Adds a service to this <see cref="ServiceRegistry"/>.
        /// </summary>
        /// <typeparam name="T">The type of service to add.</param>
        /// <param name="provider">The service provider to add.</param>
        /// <exception cref="System.ArgumentException">Service is already registered;type</exception>
        public void AddServiceAs<T>(this IServiceRegistry registry, T provider)
        {
            registry.AddService(typeof(T), provider);
        }
        
        /// <summary>
        /// Gets a service instance from a specified interface contract.
        /// </summary>
        /// <typeparam name="T">Type of the interface contract of the service</typeparam>
        /// <param name="registry">The registry.</param>
        /// <returns>An instance of the requested service registered to this registry.</returns>
        public static T GetServiceAs<T>(this IServiceRegistry registry)
        {
            return (T)registry.GetService(typeof(T));
        }

        /// <summary>
        /// Gets a service instance from a specified interface contract.
        /// </summary>
        /// <typeparam name="T">Type of the interface contract of the service</typeparam>
        /// <param name="registry">The registry.</param>
        /// <exception cref="ServiceNotFoundException">If the service was not found</exception>
        /// <returns>An instance of the requested service registered to this registry.</returns>
        public static T GetSafeServiceAs<T>(this IServiceRegistry registry)
        {
            var serviceFound = (T)registry.GetService(typeof(T));
            if (Equals(serviceFound, default(T)))
            {
                throw new ServiceNotFoundException(typeof(T));
            }
            return serviceFound;
        }

        /// <summary>
        /// Gets a service instance from a specified interface contract.
        /// </summary>
        /// <typeparam name="T">Type of the interface contract of the service</typeparam>
        /// <param name="registry">The registry.</param>
        /// <param name="serviceReady">The service ready.</param>
        /// <returns>An instance of the requested service registered to this registry.</returns>
        /// <exception cref="ServiceNotFoundException">If the service was not found</exception>
        public static void GetServiceLate<T>(this IServiceRegistry registry, Action<T> serviceReady)
        {
            var instance = GetServiceAs<T>(registry);
            if (Equals(instance, null))
            {
                var deferred = new ServiceDeferredRegister<T>(registry, serviceReady);
                deferred.Register();
            }
            else
            {
                serviceReady(instance);
            }
        }    

        /// <summary>Removes the object providing a specified service.</summary>
        /// <typeparam name="T">The type of service.</param>
        public void RemoveService<T>(this IServiceRegistry registry)
        {
            registry.RemoveService(typeof(T));
        }

        private class ServiceDeferredRegister<T>
        {
            private readonly IServiceRegistry services;
            private readonly Action<T> serviceReady;

            public ServiceDeferredRegister(IServiceRegistry registry, Action<T> serviceReady)
            {
                services = registry;
                this.serviceReady = serviceReady;
            }

            public void Register()
            {
                services.ServiceAdded += Services_ServiceAdded;
            }

            void Services_ServiceAdded(object sender, ServiceEventArgs args)
            {
                if (args.ServiceType == typeof(T))
                {
                    serviceReady((T)args.Instance);
                    services.ServiceAdded -= Services_ServiceAdded;
                }
            }
        }
    }
}
