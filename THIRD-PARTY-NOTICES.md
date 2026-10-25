# Third-party notices

RefTrace's own code is licensed under MIT. The tool also distributes the runtime
components below, which retain their original licenses and copyright notices.
This inventory is for RefTrace 0.3.0 and includes transitive runtime dependencies.
Build and test tools are not redistributed in the tool package.

| Package | Version | License | Copyright recorded by the package |
| --- | --- | --- | --- |
| Microsoft.Diagnostics.NETCore.Client | 0.2.750501 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Diagnostics.Tracing.TraceEvent | 4.0.0 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT | © Microsoft Corporation. All rights reserved. |
| Newtonsoft.Json | 13.0.3 | MIT | Copyright © James Newton-King 2008 |
| NuGet.Common | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Configuration | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.DependencyResolver.Core | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Frameworks | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.LibraryModel | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Packaging | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.ProjectModel | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Protocol | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| NuGet.Versioning | 7.9.0 | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| Spectre.Console | 0.57.2 | MIT | Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi |
| Spectre.Console.Ansi | 0.57.2 | MIT | Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi |
| System.CommandLine | 2.0.14 | MIT | © Microsoft Corporation. All rights reserved. |
| System.Security.Cryptography.Pkcs | 10.0.14 | MIT | © Microsoft Corporation. All rights reserved. |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | © Microsoft Corporation. All rights reserved. |

Original license and notice texts are included in [licenses/third-party](licenses/third-party):

- `diagnostics`: Microsoft.Diagnostics.NETCore.Client; taken from its
  [source revision](https://github.com/dotnet/diagnostics/tree/ad724c4b85e7ae775e42f393c6e95a7557a57954).
  The upstream third-party notice is included unchanged.
- `perfview`: Microsoft.Diagnostics.Tracing.TraceEvent and its bundled assemblies;
  taken from [v4.0.0](https://github.com/microsoft/perfview/tree/10e5d271cebcdf5d6cf122c6ab9c863860a7a2f1).
- `nuget`: all NuGet.* packages; the upstream copyright notice comes from their
  [source revision](https://github.com/NuGet/NuGet.Client/tree/977537e19c6be57fead1411e6cf05f936bf1baf4).
  The full [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0) is included separately.
- `spectre`: Spectre.Console and Spectre.Console.Ansi; taken from their
  [source revision](https://github.com/spectreconsole/spectre.console/tree/bbbb5729dde27b58deee44f447a788eea46ee451).
- `commandline`: System.CommandLine; taken from its
  [source revision](https://github.com/dotnet/dotnet/tree/0427b9abbead3fa64c6655eb4290c537397131fa).
- Directories named after Microsoft.Extensions.*, Newtonsoft.Json and
  System.Security.Cryptography.* preserve the license and notice files from those
  NuGet packages. Pkcs uses the same .NET Foundation MIT license text distributed
  with System.Security.Cryptography.ProtectedData.

No third-party source files have been modified. Package versions above are the ones
resolved into the tool, not a claim that every assembly is loaded for every command.
