using k8s;
using k8s.Autorest;
using k8s.Models;

using KubeOps.Operator;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TestKubeOps
{
    public static class Program
    {
        public static IKubernetes K8s { get; private set; }
        public static TestMode TestMode { get; private set; }
        public static bool Requeue { get; private set; }
        public static readonly JsonSerializerOptions serializerOptions = 
            new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

        public static async Task Main(string[] args)
        {
            // Log unhandled exceptions.

            AppDomain.CurrentDomain.UnhandledException +=
                (sender, args) =>
                {
                    LogException((Exception)args.ExceptionObject);
                };

            // Create a K8s client to be used for tests.

            K8s = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());

            // Handle config generation here.

            var command = args.FirstOrDefault()?.ToLower();

            if (command == "generator")
            {
                await CreateHostBuilder(args).Build().RunOperatorAsync(args);
                return;
            }

            // Make sure that our test CRD is registered.

            var testCrdYaml =
@"
apiVersion: apiextensions.k8s.io/v1
kind: CustomResourceDefinition
metadata:
  name: kubeopstests.neonforge.io
spec:
  group: neonforge.io
  names:
    kind: KubeOpsTest
    listKind: KubeOpsTestList
    plural: kubeopstests
    singular: kubeopstest
  scope: Cluster
  versions:
  - name: v1alpha1
    schema:
      openAPIV3Schema:
        description: Used for testing purposes.
        properties:
          status:
            properties:
              phase:
                type: string
            type: object
          spec:
            properties:
              message:
                type: string
            type: object
        type: object
    served: true
    storage: true
    subresources:
      status: {}
";
            var yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var testCrd = yamlDeserializer.Deserialize<V1CustomResourceDefinition>(testCrdYaml);

            try
            {
                await K8s.CreateCustomResourceDefinitionAsync(testCrd);
            }
            catch (HttpOperationException e)
            {
                // Ignore [Conflict] when the CRD already exists.

                if (e.Response.StatusCode != HttpStatusCode.Conflict)
                {
                    throw;
                }
            }

            // Remove any existing test resources from previous runs.

            var existingElement = (JsonElement)await K8s.ListClusterCustomObjectAsync(group: V1KubeOpsTest.KubeGroup, version: V1KubeOpsTest.KubeApiVersion, plural: V1KubeOpsTest.KubePlural);
            var existingObjects = existingElement.Deserialize<V1CustomObjectList<V1KubeOpsTest>>(serializerOptions);

            foreach (var item in existingObjects.Items)
            {
                await K8s.DeleteClusterCustomObjectAsync(group: V1KubeOpsTest.KubeGroup, version: V1KubeOpsTest.KubeApiVersion, plural: V1KubeOpsTest.KubePlural, name: item.Name());
            }

            // Start the test when KubeOps isn't generating configs after building.

            var Requeue = args.Any(arg => arg == "--requeue");

            switch (command)
            {
                case "createanddelete":

                    _  = TestCreateAndDeleteAsync();
                    break;

                case "createmodifystatus":

                    _ = TestCreateModifyStatusAsync(throwInStatusModified: false);
                    break;

                case "createmodifystatusexception":

                    _ = TestCreateModifyStatusAsync(throwInStatusModified: true);
                    break;

                case null:

                    Console.WriteLine();
                    Console.WriteLine($"*** ERROR: command expected:");
                    Console.WriteLine();
                    Console.WriteLine("USAGE:");
                    Console.WriteLine();
                    Console.WriteLine("    Create a resource once a second and the controller deletes immediately on reconcile:");
                    Console.WriteLine();
                    Console.WriteLine("        TestKubeOps CreateAndDelete [--requeue]");
                    Console.WriteLine();
                    Console.WriteLine("    Create a resource once a second and the controller updates status on reconcile:");
                    Console.WriteLine();
                    Console.WriteLine("        TestKubeOps CreateModifyStatus [--requeue]");
                    Console.WriteLine();
                    Console.WriteLine("    Create a resource once a second and the controller throws an exception on status-modified:");
                    Console.WriteLine();
                    Console.WriteLine("        TestKubeOps CreateModifyStatusException [--requeue]");
                    Console.WriteLine();
                    Environment.Exit(1);
                    break;

                default:

                    Console.WriteLine();
                    Console.WriteLine($"*** ERROR: unexpected command: {command}");
                    Environment.Exit(1);
                    break;
            }

            // Clear the arguments so KubeOps won't get confused.

            args = new string[0];

            // Start the operator.

            await CreateHostBuilder(args).Build().RunOperatorAsync(args);
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
        }

        public static void LogException(Exception e)
        {
            Console.Error.WriteLine($"EXCEPTION: {e.GetType().FullName}: {e.Message}");

            if (e is HttpOperationException httpException)
            {
                Console.Error.WriteLine($"DETAILS: {httpException.Response.Content}");
            }

            Console.Error.WriteLine($"STACK TRACE:");
            Console.Error.WriteLine($"============");
            Console.Error.WriteLine(e.StackTrace);
        }

        private static async Task PauseForStartAsync()
        {
            // Give KubeOps a chance to spin-up.

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        private static async Task TestCreateAndDeleteAsync()
        {
            TestMode = TestMode.CreateAndDelete;

            await PauseForStartAsync();

            // This test creates a new test object every second and the 
            // controller will immediately delete them as they reconciled.

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                var testObject = new V1KubeOpsTest()
                {
                    Kind       = V1KubeOpsTest.KubeKind,
                    ApiVersion = $"{V1KubeOpsTest.KubeGroup}/{V1KubeOpsTest.KubeApiVersion}",
                    Metadata   = new V1ObjectMeta()
                    {
                        Name = Guid.NewGuid().ToString("d")
                    },
                    Spec = new V1KubeOpsTest.TestSpec()
                    {
                        Message = "Hello World!"
                    }
                };

                try
                {
                    await K8s.CreateClusterCustomObjectAsync(testObject, group: V1KubeOpsTest.KubeGroup, version: V1KubeOpsTest.KubeApiVersion, plural: V1KubeOpsTest.KubePlural);
                }
                catch (Exception e)
                {
                    LogException(e);
                }
            }
        }

        private static async Task TestCreateModifyStatusAsync(bool throwInStatusModified)
        {
            TestMode = throwInStatusModified ? TestMode.CreateModifyStatusException : TestMode.CreateModifyStatus;

            await PauseForStartAsync();

            // This test creates a new test object every second.  We're going
            // remove objects older than 10 seconds below in batches.

            var lifeSpan            = TimeSpan.FromSeconds(10);
            var deleteBatchInterval = TimeSpan.FromSeconds(30);
            var nextDeleteTimeUtc   = DateTime.UtcNow + deleteBatchInterval;

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                var testObject = new V1KubeOpsTest()
                {
                    Kind       = V1KubeOpsTest.KubeKind,
                    ApiVersion = $"{V1KubeOpsTest.KubeGroup}/{V1KubeOpsTest.KubeApiVersion}",
                    Metadata   = new V1ObjectMeta()
                    {
                        Name = Guid.NewGuid().ToString("d")
                    },
                    Spec = new V1KubeOpsTest.TestSpec()
                    {
                        Message = "Hello World!"
                    }
                };

                try
                {
                    // Create the new object.

                    await K8s.CreateClusterCustomObjectAsync(testObject, group: V1KubeOpsTest.KubeGroup, version: V1KubeOpsTest.KubeApiVersion, plural: V1KubeOpsTest.KubePlural);

                    var utcNow = DateTime.UtcNow;

                    if (utcNow <= nextDeleteTimeUtc)
                    {
                        // Remove any existing objects older than 30 seconds.  Note that we're going
                        // batch deletes to make this more excitig.

                        var existingElement = (JsonElement)await K8s.ListClusterCustomObjectAsync(group: V1KubeOpsTest.KubeGroup, version: V1KubeOpsTest.KubeApiVersion, plural: V1KubeOpsTest.KubePlural);
                        var existingObjects = existingElement.Deserialize<V1CustomObjectList<V1KubeOpsTest>>(serializerOptions);

                        foreach (var item in existingObjects.Items
                            .Where(item => utcNow - item.Metadata.CreationTimestamp >= lifeSpan))
                        {
                            await K8s.DeleteClusterCustomObjectAsync(group: V1KubeOpsTest.KubeGroup, version: V1KubeOpsTest.KubeApiVersion, plural: V1KubeOpsTest.KubePlural, name: item.Name());
                        }

                        nextDeleteTimeUtc = DateTime.UtcNow + deleteBatchInterval;
                    }
                }
                catch (Exception e)
                {
                    LogException(e);
                }
            }
        }
    }
}
