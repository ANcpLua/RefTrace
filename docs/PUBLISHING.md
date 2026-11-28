# Publishing

A release is the package **RefTrace** on nuget.org, the tag `v<version>` and a GitHub release with the changelog
section of that version. The workflow `.github/workflows/publish.yml` in
[`ANcpLua/RefTrace`](https://github.com/ANcpLua/RefTrace) pushes the package. It runs only when started manually.

## Set up once

1. In GitHub, create an environment named `release`.
2. Add its `NUGET_USER` secret: `ANcpLua`, the NuGet profile name. It is neither an email address nor an API key.
3. Sign in at [NuGet Trusted Publishing](https://www.nuget.org/account/trustedpublishing) and add a policy with the
   values below. Choose the NuGet account or organization that will own the package. Set the package pattern to
   exactly `RefTrace`, keep the permission to push new packages and versions, and turn the permission to unlist or
   relist versions off.

| Field | Value |
| --- | --- |
| Repository owner | `ANcpLua` |
| Repository | `RefTrace` |
| Workflow file | `publish.yml` |
| Environment | `release` |

The policy names the workflow file; the workflow's display name plays no part. Step 3 needs the owner's NuGet
sign-in. Steps 1 and 2 can be done with `gh`:

```sh
gh api -X PUT repos/ANcpLua/RefTrace/environments/release
gh secret set NUGET_USER --env release --repo ANcpLua/RefTrace --body ANcpLua
```

The workflow uses `NuGet/login` with GitHub's `id-token: write` permission to obtain a short-lived publishing key,
so the repository stores no long-lived NuGet API key.

## Release a version

1. **Version.** `Directory.Build.props` carries the version, `CHANGELOG.md` has its section, and the `version`
   default in `publish.yml` matches. Each release needs a new version. Leave the `reftrace` pin in
   `dotnet-tools.json` at the previous release: CI restores that manifest, and NuGet lists the new version only
   after step 4.
2. **Try the product.** On the commit you want to release, pack the tool, install it from the local package as
   [Package and try locally](REFERENCE.md#package-and-try-locally) shows, and run the commands of the README with
   it. A green CI run does not replace this check. If you find a problem, fix it and start again with the new
   commit. Record `git rev-parse HEAD`.
3. **CI.** That commit is the head of `main`, and its latest "Build and test" run succeeded on every platform.
4. **Publish.** Start the workflow with the version and the full SHA from step 2:

   ```sh
   gh workflow run publish.yml --ref main -f version=<version> -f tested_commit=<full SHA>
   gh run watch "$(gh run list --workflow publish.yml --limit 1 --json databaseId --jq '.[0].databaseId')" --exit-status
   ```

   The workflow rejects any other commit, requires the green CI run, then builds, tests, packs and runs the
   packaged command again before it asks NuGet for a token.
5. **Confirm.** The version appears in `https://api.nuget.org/v3-flatcontainer/reftrace/index.json`. NuGet validates
   and indexes a new package first, which can take several minutes.
6. **Tag and GitHub release.** Tag the released commit and publish the changelog section of the version as the
   release notes:

   ```sh
   git tag v<version> <full SHA> && git push origin v<version>
   gh release create v<version> --verify-tag --title "RefTrace <version>" --notes-file <file with the changelog section>
   ```
7. **Install it.** `dotnet tool update --global RefTrace`, then `ref version` prints the version and the released
   commit.
8. **Raise the pin.** In its own commit on `main`, run `dotnet tool update --local RefTrace --version <version>` and
   push the changed `dotnet-tools.json`, so `make audit` reviews with the new release.

A version with a suffix such as `-preview.1` is a prerelease: add `--prerelease` to `gh release create` and to the
`dotnet tool` commands.

The public API is still evolving in the 0.x versions; there is no API-freeze baseline yet.

The package includes its MIT license and third-party notices. When you update dependencies, review the packaged
runtime assets and their license metadata, then update `THIRD-PARTY-NOTICES.md` and the original texts under
`licenses/third-party`.
