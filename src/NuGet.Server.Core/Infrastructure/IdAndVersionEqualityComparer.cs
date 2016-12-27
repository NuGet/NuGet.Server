// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using NuGet.Shared;

namespace NuGet.Server.Core.Infrastructure
{
    public class IdAndVersionEqualityComparer : IEqualityComparer<IServerPackage>
    {
        private static readonly IdAndVersionEqualityComparer _instance = new IdAndVersionEqualityComparer();
        public static IdAndVersionEqualityComparer Instance => _instance;

        public bool Equals(IServerPackage x, IServerPackage y)
        {
            // If both are null or the same instance, this condition will be true.
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            return StringComparer.OrdinalIgnoreCase.Equals(x.Id, y.Id) &&
                   x.Version == y.Version;
        }

        public int GetHashCode(IServerPackage obj)
        {
            var combiner = new HashCodeCombiner();

            combiner.AddStringIgnoreCase(obj.Id);
            combiner.AddObject(obj.Version);

            return combiner.CombinedHash;
        }
    }
}
