using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;

using k8s;
using k8s.Models;

namespace TestKubeOps
{
    public class V1CustomObjectList<T> : IKubernetesObject<V1ListMeta>, IKubernetesObject, IMetadata<V1ListMeta>, IItems<T>, IValidate
        where T : IKubernetesObject
    {
        /// <inheritdoc/>
        public string ApiVersion { get; set; }

        /// <inheritdoc/>
        public string Kind { get; set; }

        /// <inheritdoc/>
        public V1ListMeta Metadata { get; set; }

        /// <inheritdoc/>
        public IList<T> Items { get; set; }

        /// <inheritdoc/>
        public void Validate()
        {
        }
    }
}
