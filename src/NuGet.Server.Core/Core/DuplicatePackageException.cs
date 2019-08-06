using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Server.Core
{
    /// <summary>
    /// This exception is thrown when trying to add a package in a version that already exists on the server 
    /// and <see cref="NuGet.Server.Core.Infrastructure.ServerPackageRepository.AllowOverrideExistingPackageOnPush"/> is set to false.
    /// </summary>
    public class DuplicatePackageException : Exception
    {
        public DuplicatePackageException()
        {
        }

        public DuplicatePackageException(string message) : base(message)
        {
        }

        public DuplicatePackageException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DuplicatePackageException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
