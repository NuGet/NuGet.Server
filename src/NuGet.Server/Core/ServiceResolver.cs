// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
namespace NuGet.Server
{
    public class ServiceResolver
    {
        private static readonly object SyncLock = new object();

        public static IServiceResolver Current { get; private set; }

        private static void EnsureServiceResolver()
        {
            if (Current == null)
            {
                lock (SyncLock)
                {
                    if (Current == null)
                    {
                        Current = new DefaultServiceResolver();
                    }
                }
            }
        }

        public static void SetServiceResolver(IServiceResolver serviceResolver)
        {
            Current = serviceResolver;
        }

        public static T Resolve<T>()
            where T : class
        {
            EnsureServiceResolver();

            return Current.Resolve(typeof (T)) as T;
        }
    }
}