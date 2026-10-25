# Publishing setup

The workflow is `.github/workflows/publish.yml` in
[`ANcpLua/RefTrace`](https://github.com/ANcpLua/RefTrace). It runs only when started manually.

Before the first release:

1. In GitHub, create an environment named `release`.
2. Add its `NUGET_USER` secret: `ANcpLua`, the NuGet profile name, not an email or an API key.
3. Sign in at [NuGet Trusted Publishing](https://www.nuget.org/account/trustedpublishing)
   and add a policy for that exact repository, workflow file `publish.yml`, and environment `release`.
   Choose the NuGet account or organization that will own the package, with the appropriate
   permission for publishing RefTrace (including its first version). Set the package pattern
   to exactly `RefTrace`, keep permission to push new packages and versions, and disable
   permission to unlist or relist versions.

The workflow first builds, tests, packs and runs the packaged command. It then uses
`NuGet/login` with GitHub's `id-token: write` permission to obtain a short-lived publishing
key. There is no long-lived NuGet API key to save in the repository.

The workflow version must match `Directory.Build.props`. Each new release needs a new
version. Confirm the NuGet policy is in place before the first publishing run.

The policy values are:

| Field | Value |
| --- | --- |
| Repository owner | `ANcpLua` |
| Repository | `RefTrace` |
| Workflow file | `publish.yml` |
| Environment | `release` |

The workflow's display name is not the workflow filename used by the policy.

The package includes its MIT license and third-party notices. When updating dependencies,
review the packaged runtime assets and their license metadata, then update
`THIRD-PARTY-NOTICES.md` and the original texts under `licenses/third-party`.
