using k8s;
using KubeOps.Operator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace TestKubeOps
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddSingleton<IKubernetes>(Program.K8s)
                .AddKubernetesOperator(
                settings =>
                {
                    settings.Name                   = "TestKubeOps";
                    settings.EnableAssemblyScanning = true;
                    settings.EnableLeaderElection   = false;
                    settings.WatcherMaxRetrySeconds = 15;
                });
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseKubernetesOperator();
        }
    }
}
