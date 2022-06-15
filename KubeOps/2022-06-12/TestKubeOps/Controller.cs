using k8s;
using k8s.Models;

using KubeOps.Operator.Controller;
using KubeOps.Operator.Controller.Results;
using KubeOps.Operator.Finalizer;
using KubeOps.Operator.Rbac;

using Microsoft.AspNetCore.JsonPatch;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestKubeOps
{
    [EntityRbac(typeof(V1KubeOpsTest), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Patch | RbacVerb.Watch | RbacVerb.Update)]
    public class Controller : IResourceController<V1KubeOpsTest>
    {
        //---------------------------------------------------------------------
        // Static members

        public static DateTime? FirstReconcileTime { get; private set; } = null;

        //---------------------------------------------------------------------
        // Instance members

        private readonly IKubernetes K8s;

        public Controller(IKubernetes k8s)
        {
            this.K8s = k8s;
        }

        public async Task<ResourceControllerResult> ReconcileAsync(V1KubeOpsTest testObject)
        {
            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine($"RECONCILE: {testObject.Name()}");
            Console.WriteLine("======================================");

            switch (Program.TestMode)
            {
                case TestMode.CreateAndDelete:

                    await K8s.DeleteClusterCustomObjectAsync(group: V1KubeOpsTest.KubeGroup, version: V1KubeOpsTest.KubeApiVersion, plural: V1KubeOpsTest.KubePlural, name: testObject.Name());
                    break;

                case TestMode.CreateModifyStatus:
                case TestMode.CreateModifyStatusException:

                    if (testObject.Status?.Phase == "Created")
                    {
                        break;
                    }

                    var patch = CreatePatch<V1KubeOpsTest>();

                    patch.Replace(path => path.Status, new V1KubeOpsTest.TestStatus());
                    patch.Replace(path => path.Status.Phase, "Created");

                    await K8s.PatchClusterCustomObjectStatusAsync(ToV1Patch<V1KubeOpsTest>(patch), group: V1KubeOpsTest.KubeGroup, version: V1KubeOpsTest.KubeApiVersion, plural: V1KubeOpsTest.KubePlural, name: testObject.Name());
                    break;

                case TestMode.FirstWatchDelay:

                    if (!FirstReconcileTime.HasValue)
                    {
                        FirstReconcileTime = DateTime.UtcNow;
                    }
                    break;
            }

            return Program.Requeue ? ResourceControllerResult.RequeueEvent(TimeSpan.FromSeconds(5)) : null;
        }

        public async Task DeletedAsync(V1KubeOpsTest testObject)
        {
            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine($"DELETED: {testObject.Name()}");
            Console.WriteLine("======================================");

            switch (Program.TestMode)
            {
                case TestMode.CreateAndDelete:

                    break;

                case TestMode.CreateModifyStatus:

                    break;

                case TestMode.CreateModifyStatusException:
                    
                    break;
            }

            await Task.CompletedTask;
        }

        public async Task StatusModifiedAsync(V1KubeOpsTest testObject)
        {
            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine($"STATUS-MODIFIED: {testObject.Name()}");
            Console.WriteLine("======================================");

            switch (Program.TestMode)
            {
                case TestMode.CreateAndDelete:

                    break;

                case TestMode.CreateModifyStatus:

                    break;

                case TestMode.CreateModifyStatusException:

                    throw new Exception("TEST EXCEPTION");
            }

            await Task.CompletedTask;
        }

        //---------------------------------------------------------------------
        // Helpers:

        private static readonly JsonSerializerSettings jsonNetSettings = new JsonSerializerSettings();

        /// <summary>
        /// Creates a new <see cref="JsonPatchDocument"/> that can be used to specify modifications
        /// to a <typeparamref name="T"/> custom object.
        /// </summary>
        /// <typeparam name="T">Specifies the custom object type.</typeparam>
        /// <returns>The <see cref="JsonPatchDocument"/>.</returns>
        public static JsonPatchDocument<T> CreatePatch<T>()
            where T : class
        {
            return new JsonPatchDocument<T>()
            {
                ContractResolver = new DefaultContractResolver()
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            };
        }

        /// <summary>
        /// Converts a <see cref="JsonPatchDocument"/> into a <see cref="V1Patch"/> that
        /// can be submitted to the Kubernetes API.
        /// </summary>
        /// <typeparam name="T">Identifies the type being patched.</typeparam>
        /// <param name="patchDoc">The configured patch document.</param>
        /// <returns>The <see cref="V1Patch"/> instance.</returns>
        public static V1Patch ToV1Patch<T>(JsonPatchDocument<T> patchDoc)
            where T : class
        {
            var patchJson = JsonConvert.SerializeObject(patchDoc, Formatting.None, jsonNetSettings);

            return new V1Patch(patchJson, V1Patch.PatchType.JsonPatch);
        }
    }
}
