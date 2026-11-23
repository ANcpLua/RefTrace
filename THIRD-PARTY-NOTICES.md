# Third-party notices

RefTrace's own code is licensed under MIT. The tool also distributes the runtime
components below, which retain their original licenses and copyright notices.
This inventory is for RefTrace 0.10.0 and includes transitive runtime dependencies.
Build, test and audit tools are not redistributed in the tool package.

| Package | Version | License | Copyright recorded by the package |
| --- | --- | --- | --- |
| Microsoft.Diagnostics.NETCore.Client | 0.2.750501 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Diagnostics.Tracing.TraceEvent | 4.0.0 | MIT | © Microsoft Corporation. All rights reserved. |
| Newtonsoft.Json | 14.0.1-beta2 | MIT | Copyright © James Newton-King 2008 |
| NuGet.Common | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Configuration | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.DependencyResolver.Core | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Frameworks | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.LibraryModel | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Packaging | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.ProjectModel | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Protocol | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Versioning | 8.0.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| Spectre.Console | 0.57.3-alpha.0.29 | MIT | Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi |
| Spectre.Console.Ansi | 0.57.3-alpha.0.29 | MIT | Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi |
| System.CommandLine | 3.0.0 | MIT | © Microsoft Corporation. All rights reserved. |
| System.Security.Cryptography.Pkcs | 11.0.0 | MIT | © Microsoft Corporation. All rights reserved. |
| System.Security.Cryptography.ProtectedData | 11.0.0 | MIT | © Microsoft Corporation. All rights reserved. |

Original license and notice texts are included in [licenses/third-party](licenses/third-party):

- `diagnostics`: Microsoft.Diagnostics.NETCore.Client; from its
  [source revision](https://github.com/dotnet/diagnostics/tree/ad724c4b85e7ae775e42f393c6e95a7557a57954).
- `perfview`: TraceEvent and bundled assemblies; from
  [v4.0.0](https://github.com/microsoft/perfview/tree/10e5d271cebcdf5d6cf122c6ab9c863860a7a2f1).
- `nuget`: NuGet.* packages; from their
  [source revision](https://github.com/NuGet/NuGet.Client/tree/7719b2079546f52df28ecb639ce114ca8ca82440),
  with the full Apache License 2.0.
- `spectre`: Spectre.Console and Spectre.Console.Ansi; from their
  [source revision](https://github.com/spectreconsole/spectre.console/tree/0de1831405299575419a9cb17d5e7349a3ae61f5).
- `commandline`: System.CommandLine; from its
  [source revision](https://github.com/dotnet/dotnet/tree/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f).
- Directories named after Newtonsoft.Json and System.Security.Cryptography.* preserve
  their package license and notice texts. Pkcs uses the same .NET Foundation MIT
  license text distributed with System.Security.Cryptography.ProtectedData.

Third-party source files have not been modified. These are the packages distributed
with the tool, not a claim that every assembly is loaded by every command.
