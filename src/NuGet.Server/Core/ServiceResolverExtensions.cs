// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace NuGet.Server
{
    public static class ServiceResolverExtensions
    {
        public static T Resolve<T>(this IServiceResolver resolver)
            where T : class
        {
            return resolver.Resolve(typeof(T)) as T;
        }
    }
}