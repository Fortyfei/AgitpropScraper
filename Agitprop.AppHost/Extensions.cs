using System;

using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes.Swarm;

namespace Agitprop.AppHost;

public static class Extensions
{
    extension<T>(IResourceBuilder<T> builder) where T : IComputeResource
    {
        /// <summary>
        /// Configures environment-aware image naming for container push operations. In CI
        /// (GITHUB_SHA set) the image is tagged with the commit, which is what makes a deployment
        /// rollback-able: the release artifact pins the images it was generated with instead of a
        /// moving "latest". Locally the name gains a "-dev" suffix so a developer push cannot
        /// overwrite the images a deployment runs.
        /// </summary>
        internal IResourceBuilder<T> WithEnvironmentAwareImagePush()
        {
            return builder.WithImagePushOptions(async context =>
            {
                var commit = Environment.GetEnvironmentVariable("GITHUB_SHA");

                if (string.IsNullOrWhiteSpace(commit))
                {
                    context.Options.RemoteImageName += "-dev";
                    context.Options.RemoteImageTag = "latest";
                }
                else
                {
                    context.Options.RemoteImageTag = commit;
                }

                await Task.CompletedTask;
            });
        }

        /// <summary>
        /// Applies the hardening every published service shares: restart on failure and no
        /// privilege escalation inside the container. Only has an effect when publishing.
        /// </summary>
        internal IResourceBuilder<T> HardenComposeService()
        {
            return builder.PublishAsDockerComposeService((_, service) =>
            {
                service.Restart = "unless-stopped";
                service.SecurityOpt = ["no-new-privileges:true"];
            });
        }

        /// <summary>
        /// Declares a Docker health check for a published container. Aspire only emits one for the
        /// HTTP endpoints of projects, so containers published as images need theirs from here.
        /// Dependants wait for <c>service_healthy</c> once this is present.
        /// </summary>
        internal IResourceBuilder<T> WithComposeHealthCheck(params string[] test)
        {
            return builder.PublishAsDockerComposeService((_, service) =>
            {
                service.Healthcheck = new Healthcheck
                {
                    Test = [.. test],
                    Interval = "15s",
                    Timeout = "5s",
                    Retries = 5,
                    StartPeriod = "60s"
                };
            });
        }

        /// <summary>
        /// Upgrades the compose dependency on <paramref name="serviceName"/> from
        /// <c>service_started</c> to <c>service_healthy</c>, so a first boot cannot race a
        /// database that is up but not yet accepting connections.
        /// </summary>
        internal IResourceBuilder<T> WaitForHealthyDependency(string serviceName)
        {
            return builder.PublishAsDockerComposeService((_, service) =>
            {
                if (service.DependsOn is not null &&
                    service.DependsOn.TryGetValue(serviceName, out var dependency))
                {
                    dependency.Condition = "service_healthy";
                }
            });
        }

        /// <summary>
        /// Caps the memory a published container may use, so one runaway worker cannot push the
        /// host into the OOM killer and take the database down with it.
        /// </summary>
        internal IResourceBuilder<T> LimitComposeMemory(string memory)
        {
            return builder.PublishAsDockerComposeService((_, service) =>
            {
                service.Deploy ??= new Deploy();
                service.Deploy.Resources ??= new Resources();
                service.Deploy.Resources.Limits = new ResourceSpec { Memory = memory };
            });
        }

        /// <summary>
        /// Drops every Linux capability from a published container. The default set still
        /// contains CHOWN, SETUID and SETGID, which a compromised process could use even though
        /// these services run as a non-root user and never need a privileged operation.
        /// </summary>
        internal IResourceBuilder<T> WithoutCapabilities()
        {
            return builder.PublishAsDockerComposeService((_, service) => service.CapDrop = ["ALL"]);
        }

        /// <summary>
        /// Mounts a named volume into a published project. The container-only volume overloads do
        /// not apply to projects, which become containers only when publishing.
        /// </summary>
        internal IResourceBuilder<T> WithPublishedVolume(string name, string target)
        {
            builder.Resource.Annotations.Add(
                new ContainerMountAnnotation(name, target, ContainerMountType.Volume, isReadOnly: false));

            return builder;
        }
    }

    extension<T>(IResourceBuilder<T> builder) where T : IResourceWithEndpoints
    {
        /// <summary>
        /// Drops the HTTPS endpoint of a published project. TLS terminates at the Cloudflare edge,
        /// so the container needs no HTTPS endpoint, and its port would otherwise be a second,
        /// unfilled value in the deployment bundle.
        /// </summary>
        internal IResourceBuilder<T> WithoutHttpsEndpoint()
        {
            foreach (var annotation in builder.Resource.Annotations.OfType<EndpointAnnotation>()
                                        .Where(annotation => annotation.UriScheme == "https")
                                        .ToArray())
            {
                builder.Resource.Annotations.Remove(annotation);
            }

            return builder;
        }
    }
}
