// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http.OData;
using System.Web.Http.OData.Extensions;
using System.Web.Http.OData.Formatter.Serialization;
using System.Web.Http.OData.Routing;
using Microsoft.Data.OData;
using Microsoft.Data.OData.Atom;
using NuGet.Server.Core.DataServices;

namespace NuGet.Server.V2.OData.Serializers
{
    public class NuGetEntityTypeSerializer
        : ODataEntityTypeSerializer
    {
        public NuGetEntityTypeSerializer(ODataSerializerProvider serializerProvider)
            : base(serializerProvider)
        {
            ContentType = "application/zip";
        }

        public override ODataEntry CreateEntry(SelectExpandNode selectExpandNode, EntityInstanceContext entityInstanceContext)
        {
            var entry = base.CreateEntry(selectExpandNode, entityInstanceContext);

            TryAnnotateV2FeedPackage(entry, entityInstanceContext);

            return entry;
        }

        private void TryAnnotateV2FeedPackage(ODataEntry entry, EntityInstanceContext entityInstanceContext)
        {
            var instance = entityInstanceContext.EntityInstance as ODataPackage;

            if (instance != null)
            {
                // Set Atom entry metadata
                var atomEntryMetadata = new AtomEntryMetadata();
                atomEntryMetadata.Title = instance.Id;
                if (!string.IsNullOrEmpty(instance.Authors))
                {
                    atomEntryMetadata.Authors = new[] { new AtomPersonMetadata { Name = instance.Authors } };
                }
                if (instance.LastUpdated > DateTime.MinValue)
                {
                    atomEntryMetadata.Updated = instance.LastUpdated;
                }
                if (instance.Published > DateTime.MinValue)
                {
                    atomEntryMetadata.Published = instance.Published;
                }
                if (!string.IsNullOrEmpty(instance.Summary))
                {
                    atomEntryMetadata.Summary = instance.Summary;
                }
                entry.SetAnnotation(atomEntryMetadata);

                // Set the ID and links. We have to do this because the self link should have a version containing
                // SemVer 2.0.0 metadata (e.g. 1.0.0+git).
                entry.Id = BuildId(instance, entityInstanceContext);
                entry.ReadLink = new Uri(entry.Id);
                entry.EditLink = new Uri(entry.Id);
                
                // Add package download link
                entry.MediaResource = new ODataStreamReferenceValue
                {
                    ContentType = ContentType,
                    ReadLink = BuildLinkForStreamProperty(instance, entityInstanceContext)
                };

                // Make the download action target match the media resource link.
                entry.Actions = entry
                    .Actions
                    .Select(action =>
                    {
                        if (StringComparer.OrdinalIgnoreCase.Equals("Download", action.Title))
                        {
                            return new ODataAction
                            {
                                Metadata = action.Metadata,
                                Target = entry.MediaResource.ReadLink,
                                Title = action.Title
                            };
                        }

                        return action;
                    })
                    .ToList();
            }
        }

        public string ContentType { get; }
        
        private string BuildId(ODataPackage package, EntityInstanceContext context)
        {
            var segments = GetPackagePathSegments(package);
            return context.Url.CreateODataLink(segments);
        }

        private  Uri BuildLinkForStreamProperty(ODataPackage package, EntityInstanceContext context)
        {
            var segments = GetPackagePathSegments(package);
            segments.Add(new ActionPathSegment("Download"));
            var downloadUrl = context.Url.CreateODataLink(segments);
            return new Uri(downloadUrl);
        }

        private static List<ODataPathSegment> GetPackagePathSegments(ODataPackage package)
        {
            var keyValue = "Id='" + package.Id + "',Version='" + RemoveVersionMetadata(package.Version) + "'";

            var segments = new List<ODataPathSegment>
            {
                new EntitySetPathSegment("Packages"),
                new KeyValuePathSegment(keyValue)
            };

            return segments;
        }

        private static string RemoveVersionMetadata(string version)
        {
            var plusIndex = version.IndexOf('+');
            if (plusIndex >= 0)
            {
                version = version.Substring(0, plusIndex);
            }

            return version;
        }
    }
}
