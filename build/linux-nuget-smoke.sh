#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PACKAGE_OUTPUT_DIR="${1:-$REPO_ROOT/artifacts/nuget-linux-ci}"
SMOKE_ROOT="$REPO_ROOT/artifacts/linux-nuget-smoke-app"
SMOKE_PROJECT_DIR="$SMOKE_ROOT/LinuxNuGetSmoke"
SMOKE_PROJECT_FILE="$SMOKE_PROJECT_DIR/LinuxNuGetSmoke.csproj"

echo "============================================================"
echo "  CycloneDDS.NET Linux NuGet Smoke"
echo "============================================================"

mkdir -p "$PACKAGE_OUTPUT_DIR"

echo "[1/4] Packing CycloneDDS.NET..."
dotnet pack "$REPO_ROOT/src/CycloneDDS.Runtime/CycloneDDS.Runtime.csproj" \
  -c Release \
  -o "$PACKAGE_OUTPUT_DIR" \
  --nologo

echo "[2/4] Creating clean consumer app..."
rm -rf "$SMOKE_ROOT"
dotnet new console \
  --framework net8.0 \
  --name LinuxNuGetSmoke \
  --output "$SMOKE_PROJECT_DIR" \
  --no-restore

echo "[3/4] Installing package from local artifacts..."
dotnet add "$SMOKE_PROJECT_FILE" package CycloneDDS.NET \
  --prerelease \
  --source "$PACKAGE_OUTPUT_DIR"

# Generated serializers use unsafe code.
sed -i '/<Nullable>enable<\/Nullable>/a\    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>' "$SMOKE_PROJECT_FILE"

cat > "$SMOKE_PROJECT_DIR/Program.cs" <<'EOF'
using CycloneDDS.Runtime;
using CycloneDDS.Schema;

namespace LinuxNuGetSmoke;

[DdsTopic("NuGetSmokeMessage")]
public partial struct SmokeMessage
{
    public int Id;
    public int Value;
}

public static class Program
{
    public static void Main()
    {
        var topic = $"NuGetSmoke_{Guid.NewGuid():N}";
        const int expectedId = 9011;
        const int expectedValue = 654321;

        Console.WriteLine("Starting Linux NuGet smoke test...");
        Console.WriteLine($"Topic: {topic}");

        using var participant = new DdsParticipant(0);
        using var writer = new DdsWriter<SmokeMessage>(participant, topic);
        using var reader = new DdsReader<SmokeMessage>(participant, topic);

        writer.Write(new SmokeMessage
        {
            Id = expectedId,
            Value = expectedValue
        });

        Thread.Sleep(600);

        using var samples = reader.Take();
        if (samples.Count <= 0)
        {
            throw new InvalidOperationException("No samples received.");
        }

        for (var i = 0; i < samples.Count; i++)
        {
            if (samples.Infos[i].ValidData == 0)
            {
                continue;
            }

            if (samples[i].Id == expectedId && samples[i].Value == expectedValue)
            {
                Console.WriteLine("SUCCESS: CycloneDDS NuGet Linux roundtrip OK.");
                return;
            }
        }

        throw new InvalidOperationException("Received samples, but payload did not match expected values.");
    }
}
EOF

echo "[4/4] Running end-to-end NuGet consumer test..."
dotnet run --project "$SMOKE_PROJECT_FILE" -c Release

echo "Linux NuGet smoke finished successfully."
