DOTNET ?= dotnet
UV ?= uv

.PHONY: build test audit audit-catalog audit-test inspect pack
build:
	$(DOTNET) build RefTrace.slnx -c Release "-bl:artifacts/build-{}.binlog"
test:
	@set -eu; mkdir -p artifacts; results=$$(mktemp -d artifacts/test-results.XXXXXX); \
	$(DOTNET) test --solution RefTrace.slnx -c Release --report-trx --results-directory "$$results" "-bl:artifacts/test-{}.binlog"
audit: build
	$(DOTNET) run --project src/RefTrace.Cli -c Release --no-build -- review RefTrace.slnx
audit-catalog: build
	$(DOTNET) run --project src/RefTrace.Cli -c Release --no-build -- review RefTrace.slnx --catalog-only
audit-test:
	$(UV) run --frozen python -m unittest discover -s tooling/tests -v
inspect:
	jb inspectcode RefTrace.slnx --swea --settings=RefTrace.sln.DotSettings --output=artifacts/inspectcode.sarif
pack:
	$(DOTNET) pack src/RefTrace.Cli -c Release -o artifacts/packages "-bl:artifacts/pack-{}.binlog"
