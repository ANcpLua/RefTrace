DOTNET ?= dotnet
UV ?= uv

.PHONY: check restore build test audit audit-catalog audit-test tools inspect pack
build: restore
	$(DOTNET) build RefTrace.slnx -c Release --no-restore "-bl:artifacts/build-{}.binlog"
restore:
	$(DOTNET) restore RefTrace.slnx -p:Configuration=Release
test: build
	@set -eu; mkdir -p artifacts; results=$$(mktemp -d artifacts/test-results.XXXXXX); \
	$(DOTNET) test --solution RefTrace.slnx -c Release --no-build --report-trx --results-directory "$$results" "-bl:artifacts/test-{}.binlog"
audit: build
	$(DOTNET) run --project src/RefTrace.Cli -c Release --no-build -- review RefTrace.slnx
audit-catalog: build
	$(DOTNET) run --project src/RefTrace.Cli -c Release --no-build -- review RefTrace.slnx --catalog-only
audit-test:
	$(UV) run --frozen python -m unittest discover -s tooling/tests -v
tools:
	$(DOTNET) tool restore
inspect: build tools
	@set -eu; rm -f artifacts/inspectcode.sarif; report=$$(mktemp artifacts/inspectcode.XXXXXX); trap 'rm -f "$$report"' EXIT; \
	status=0; \
	$(DOTNET) tool run jb -- inspectcode RefTrace.slnx --no-build --properties:Configuration=Release \
	  --caches-home=artifacts/inspectcode-cache --LogFolder=artifacts/inspectcode-logs \
	  --swea --settings=RefTrace.sln.DotSettings --severity=SUGGESTION --format=Sarif --output="$$report" || status=$$?; \
	if [ ! -s "$$report" ]; then echo "InspectCode produced no SARIF report." >&2; exit 1; fi; \
	$(UV) run --frozen python tooling/check_inspectcode.py "$$report" || status=$$?; \
	mv "$$report" artifacts/inspectcode.sarif; exit $$status
check: test audit-test inspect
pack:
	$(DOTNET) pack src/RefTrace.Cli -c Release -o artifacts/packages "-bl:artifacts/pack-{}.binlog"
