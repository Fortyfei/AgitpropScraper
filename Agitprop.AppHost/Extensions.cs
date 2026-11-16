using System;

namespace Agitprop.AppHost;

public static class Extensions
{
    extension<T>(IResourceBuilder<T> builder) where T : IComputeResource
    {
        /// <summary>
        /// Configures environment-aware image naming for container push operations.
        /// When running locally (no GITHUB_ACTIONS env var), appends "-dev" suffix to image name and uses "latest-dev" tag.
        /// In CI (GITHUB_ACTIONS=true), uses original image name with "latest" tag.
        /// </summary>
        internal IResourceBuilder<T> WithEnvironmentAwareImagePush()
        {
            return builder.WithImagePushOptions(async context =>
            {
                context.Options.RemoteImageTag = "latest";

                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null)
                {
                    context.Options.RemoteImageName += "-dev";
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
    }
}
