using System;
using System.Collections.Generic;
using System.Text;

using k8s;
using k8s.Models;

using DotnetKubernetesClient.Entities;
using KubeOps.Operator.Entities;
using KubeOps.Operator.Entities.Annotations;

namespace TestKubeOps
{
    [KubernetesEntity(Group = KubeGroup, ApiVersion = KubeApiVersion, Kind = KubeKind, PluralName = KubePlural)]
    [KubernetesEntityShortNames]
    [EntityScope(EntityScope.Cluster)]
    [Description("Used for testing purposes.")]
    public class V1KubeOpsTest : CustomKubernetesEntity<V1KubeOpsTest.TestSpec, V1KubeOpsTest.TestStatus>
    {
        /// <summary>
        /// Object API group.
        /// </summary>
        public const string KubeGroup = "neonforge.io";

        /// <summary>
        /// Object API version.
        /// </summary>
        public const string KubeApiVersion = "v1alpha1";

        /// <summary>
        /// Object API kind.
        /// </summary>
        public const string KubeKind = "KubeOpsTest";

        /// <summary>
        /// Object plural name.
        /// </summary>
        public const string KubePlural = "kubeopstests";

        /// <summary>
        /// Default constructor.
        /// </summary>
        public V1KubeOpsTest()
        {
            ApiVersion = $"{KubeGroup}/{KubeKind}";
            Kind       = KubeKind;
        }

        /// <summary>
        /// The node execute task specification.
        /// </summary>
        public class TestSpec
        {
            public string Message { get; set; }
        }

        public class TestStatus
        {
            public String Phase { get; set; }
        }
    }
}
