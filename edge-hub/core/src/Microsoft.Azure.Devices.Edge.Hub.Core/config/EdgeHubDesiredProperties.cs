// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.Core.Config
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Azure.Devices.Edge.Util;
    using Newtonsoft.Json;

    public class EdgeHubDesiredProperties
    {
        [JsonConstructor]
        public EdgeHubDesiredProperties(string schemaVersion, IDictionary<string, RouteConfiguration> routes, StoreAndForwardConfiguration storeAndForwardConfiguration)
        {
            this.SchemaVersion = Preconditions.CheckNonWhiteSpace(schemaVersion, nameof(schemaVersion));
            this.Routes = Preconditions.CheckNotNull(routes, nameof(routes));
            this.StoreAndForwardConfiguration = Preconditions.CheckNotNull(storeAndForwardConfiguration, nameof(storeAndForwardConfiguration));

            this.ValidateSchemaVersion();
        }

        public string SchemaVersion { get; }

        [JsonConverter(typeof(RouteConfigurationConverter))]
        public IDictionary<string, RouteConfiguration> Routes;

        public StoreAndForwardConfiguration StoreAndForwardConfiguration { get; }

        internal void ValidateSchemaVersion()
        {
            if (string.IsNullOrWhiteSpace(this.SchemaVersion) || !Version.TryParse(this.SchemaVersion, out Version version))
            {
                throw new InvalidSchemaVersionException($"Invalid deployment schema version {this.SchemaVersion}");
            }

            // Check major version and upper bound
            if (version.Major != Core.Constants.ConfigSchemaVersion.Major ||
                version > Core.Constants.ConfigSchemaVersion)
            {
                throw new InvalidSchemaVersionException($"The deployment schema version {this.SchemaVersion} is not compatible with the expected version {Core.Constants.ConfigSchemaVersion}");
            }
        }
    }
}
